using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Game
{
    /// <summary>
    /// 资源发布上传链：POST /upyun/credentials 取又拍云直传凭证（policy/authorization 签名）→
    /// 表单直传文件服务（仅 https + *.upyun.com 域，防签名明文/中间人）→ POST /upload 登记文件信息。
    /// 客户端池用 HttpClientProvider.Upload（无超时+取消令牌兜底，见其注释）；凭证/登记
    /// 小请求的 30s 有限超时由工作流侧 CancelAfter 提供。
    /// </summary>
    internal sealed class CommunityUploadClient
    {
        private const string UpyunFileDomain = "https://file.suancaixianyu.cn";
        private readonly HttpClient m_httpClient;

        public CommunityUploadClient(HttpClient httpClient)
        {
            m_httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        public async Task<UpyunCredentials> GetUpyunCredentialsAsync(
            string apiBaseUrl,
            string token,
            string saveKey,
            CommunityFileType typeId,
            CancellationToken cancellationToken = default)
        {
            string body = JsonSerializer.Serialize(new UpyunCredentialsRequest
            {
                TypeId = (int)typeId,
                SaveKey = saveKey
            });
            using HttpRequestMessage request = new(HttpMethod.Post, $"{apiBaseUrl}/upyun/credentials");
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            ApplyAuthorizationHeader(request, token);
            using HttpResponseMessage response = await m_httpClient.SendAsync(request, cancellationToken);
            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                NotifyIfUnauthorized(response.StatusCode, token);
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusCommunityUploadClient", "GetUploadCredentialsFailed"), response.StatusCode, responseBody));
            }

            // 空/非 JSON body（网关 200 空响应、HTML 错误页）归一化为 InvalidOperationException，
            // 不让裸 JsonException 逃到调用方（同 CommunityApiClient 的翻译约定）。
            if (string.IsNullOrWhiteSpace(responseBody))
            {
                throw new InvalidOperationException($"{response.StatusCode}: <empty body>");
            }
            UpyunCredentialsResponse wrapped;
            UpyunCredentials credentials;
            try
            {
                wrapped = JsonSerializer.Deserialize<UpyunCredentialsResponse>(responseBody);
                credentials = wrapped?.Data
                    ?? JsonSerializer.Deserialize<UpyunCredentials>(responseBody);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"{response.StatusCode}: {ex.Message}", ex);
            }
            if (credentials == null
                || string.IsNullOrWhiteSpace(credentials.Policy)
                || string.IsNullOrWhiteSpace(credentials.Authorization)
                || string.IsNullOrWhiteSpace(credentials.UploadUrl))
            {
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusCommunityUploadClient", "UploadCredentialsResponseMalformed"), responseBody));
            }
            return credentials;
        }

        public async Task<string> UploadToUpyunAsync(
            UpyunCredentials credentials,
            Stream sourceStream,
            string fileName,
            Action<float> onProgress,
            CancellationToken cancellationToken = default)
        {
            EnsureUpyunUploadHost(credentials.UploadUrl);

            using MultipartFormDataContent form = new();
            form.Add(new StringContent(credentials.Policy), "policy");
            form.Add(new StringContent(credentials.Authorization), "authorization");

            ProgressStreamContent fileContent = new(sourceStream, 81920, onProgress);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(fileContent, "file", fileName);

            using HttpResponseMessage response = await m_httpClient.PostAsync(credentials.UploadUrl, form, cancellationToken);
            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusCommunityUploadClient", "FileServerUploadFailed"), response.StatusCode, responseBody));
            }

            string uploadedUrl = ParseUpyunUploadedUrl(responseBody);
            if (string.IsNullOrWhiteSpace(uploadedUrl))
            {
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusCommunityUploadClient", "FileServerReturnedNoLink"), responseBody));
            }
            return NormalizeUpyunUrl(uploadedUrl);
        }

        public async Task<UploadFileData> RegisterUploadedFileAsync(
            string apiBaseUrl,
            string token,
            string uploadedUrl,
            CommunityFileType typeId,
            CancellationToken cancellationToken = default)
        {
            string body = JsonSerializer.Serialize(new UploadRegisterRequest
            {
                Path = uploadedUrl,
                Type = (int)typeId
            });
            using HttpRequestMessage request = new(HttpMethod.Post, $"{apiBaseUrl}/upload");
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            ApplyAuthorizationHeader(request, token);
            using HttpResponseMessage response = await m_httpClient.SendAsync(request, cancellationToken);
            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                NotifyIfUnauthorized(response.StatusCode, token);
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusCommunityUploadClient", "RegisterUploadFileFailed"), response.StatusCode, responseBody));
            }

            if (string.IsNullOrWhiteSpace(responseBody))
            {
                throw new InvalidOperationException($"{response.StatusCode}: <empty body>");
            }
            UploadRegisterResponse registerResponse;
            try
            {
                registerResponse = JsonSerializer.Deserialize<UploadRegisterResponse>(responseBody);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"{response.StatusCode}: {ex.Message}", ex);
            }
            if (registerResponse?.Code != 200 || registerResponse.Data == null)
            {
                throw new InvalidOperationException(registerResponse?.Msg ?? string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusCommunityUploadClient", "RegisterUploadFileResponseMalformed"), responseBody));
            }
            return registerResponse.Data;
        }

        // 带 Bearer 的请求收到 401/403 说明 token 已失效：广播给订阅方统一登出+提示。
        private static void NotifyIfUnauthorized(HttpStatusCode statusCode, string token)
        {
            if (!string.IsNullOrWhiteSpace(token) && CommunityUnauthorizedNotifier.IsUnauthorized(statusCode))
            {
                CommunityUnauthorizedNotifier.Notify(token);
            }
        }

        private static void ApplyAuthorizationHeader(HttpRequestMessage request, string token)
        {
            if (!string.IsNullOrWhiteSpace(token))
            {
                request.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);
            }
        }

        private static void EnsureUpyunUploadHost(string uploadUrl)
        {
            if (!Uri.TryCreate(uploadUrl, UriKind.Absolute, out Uri uri)
                // 仅 https：http 明文传输会暴露又拍云签名（policy/authorization），
                // 且上传内容可被中间人篡改；又拍云服务端凭证下发均为 https。
                || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || (!string.Equals(uri.Host, "upyun.com", StringComparison.OrdinalIgnoreCase)
                    && !uri.Host.EndsWith(".upyun.com", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusCommunityUploadClient", "InvalidUploadUrl"), uploadUrl));
            }
        }

        private static string ParseUpyunUploadedUrl(string responseBody)
        {
            try
            {
                UpyunUploadResponse response = JsonSerializer.Deserialize<UpyunUploadResponse>(responseBody);
                if (response?.Code == 200)
                {
                    return response.Url;
                }
                if (!string.IsNullOrWhiteSpace(response?.Message))
                {
                    throw new InvalidOperationException(response.Message);
                }
            }
            catch (JsonException)
            {
                // 又拍云有时返回查询字符串形式，下面继续按旧格式解析。
            }

            const string marker = "url=";
            int index = responseBody.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return string.Empty;
            }
            int start = index + marker.Length;
            int end = responseBody.IndexOf('&', start);
            string rawUrl = end >= 0 ? responseBody.Substring(start, end - start) : responseBody.Substring(start);
            return Uri.UnescapeDataString(rawUrl);
        }

        private static string NormalizeUpyunUrl(string pathOrUrl)
        {
            if (string.IsNullOrWhiteSpace(pathOrUrl))
            {
                return string.Empty;
            }
            if (pathOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || pathOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return pathOrUrl;
            }
            return $"{UpyunFileDomain}{(pathOrUrl.StartsWith("/") ? pathOrUrl : "/" + pathOrUrl)}";
        }

        private sealed class ProgressStreamContent : HttpContent
        {
            private readonly Stream m_sourceStream;
            private readonly int m_bufferSize;
            private readonly Action<float> m_progressHandler;

            public ProgressStreamContent(Stream sourceStream, int bufferSize, Action<float> progressHandler)
            {
                m_sourceStream = sourceStream;
                m_bufferSize = bufferSize;
                m_progressHandler = progressHandler;
            }

            override protected Task SerializeToStreamAsync(Stream stream, TransportContext context)
                => SerializeToStreamAsync(stream, context, CancellationToken.None);

            // .NET 5+ 的 HttpContent 在带 CancellationToken 发送时调用此重载；仅重写旧的无 CT 重载
            // 会让上层 PostAsync 的取消令牌无法介入读写循环，大文件上传取消只能靠底层连接中止。
            override protected async Task SerializeToStreamAsync(Stream stream, TransportContext context, CancellationToken cancellationToken)
            {
                byte[] buffer = new byte[m_bufferSize];
                long uploaded = 0;
                long total = m_sourceStream.CanSeek ? m_sourceStream.Length : -1;
                int read;
                while ((read = await m_sourceStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await stream.WriteAsync(buffer, 0, read, cancellationToken);
                    uploaded += read;
                    if (total > 0)
                    {
                        m_progressHandler?.Invoke(uploaded * 100f / total);
                    }
                }
            }

            override protected bool TryComputeLength(out long length)
            {
                if (m_sourceStream.CanSeek)
                {
                    length = m_sourceStream.Length;
                    return true;
                }
                length = -1;
                return false;
            }
        }
    }
}
