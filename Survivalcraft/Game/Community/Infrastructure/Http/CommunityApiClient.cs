using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Game
{
    internal sealed class CommunityApiClient
    {
        private readonly HttpClient m_httpClient;

        public CommunityApiClient(HttpClient httpClient)
        {
            m_httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        public async Task<T> GetJsonAsync<T>(string url, string token, CancellationToken cancellationToken = default)
        {
            CommunityApiResponse<T> result = await GetJsonResultAsync<T>(url, token, cancellationToken);
            if (!result.IsSuccessStatusCode)
            {
                // 广播已由 GetJsonResultAsync 统一触发，此处不再重复。
                throw new InvalidOperationException(result.ErrorSummary);
            }

            return result.Value;
        }

        public async Task<string> GetStringAsync(string url, string token = null, CancellationToken cancellationToken = default)
        {
            using HttpRequestMessage request = new(HttpMethod.Get, url);
            if (!string.IsNullOrWhiteSpace(token))
            {
                request.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);
            }

            using HttpResponseMessage response = await m_httpClient.SendAsync(request, cancellationToken);
            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                NotifyIfUnauthorized(response.StatusCode, token);
                string errorBody = TruncateBody(body);
                throw new InvalidOperationException($"{response.StatusCode}: {errorBody}");
            }

            return body;
        }

        public async Task<TResponse> PostJsonAsync<TResponse>(string url, string token, object payload, CancellationToken cancellationToken = default)
        {
            string body = JsonSerializer.Serialize(payload);
            using HttpRequestMessage request = new(HttpMethod.Post, url);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            if (!string.IsNullOrWhiteSpace(token))
            {
                request.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);
            }

            using HttpResponseMessage response = await m_httpClient.SendAsync(request, cancellationToken);
            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                NotifyIfUnauthorized(response.StatusCode, token);
                string errorBody = TruncateBody(body);
                throw new InvalidOperationException($"{response.StatusCode}: {errorBody}");
            }
            // 空/非 JSON body（网关 200 空响应、HTML 错误页）归一化为 InvalidOperationException，
            // 不让裸 JsonException 逃到调用方（同 CommunityAuthClient 的翻译约定）。
            if (string.IsNullOrWhiteSpace(responseBody))
            {
                throw new InvalidOperationException($"{response.StatusCode}: <empty body>");
            }
            try
            {
                return JsonSerializer.Deserialize<TResponse>(responseBody);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"{response.StatusCode}: {ex.Message}", ex);
            }
        }

        public async Task<CommunityApiResponse<T>> GetJsonResultAsync<T>(string url, string token, CancellationToken cancellationToken = default)
        {
            using HttpRequestMessage request = new(HttpMethod.Get, url);
            if (!string.IsNullOrWhiteSpace(token))
            {
                request.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);
            }

            using HttpResponseMessage response = await m_httpClient.SendAsync(request, cancellationToken);
            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                NotifyIfUnauthorized(response.StatusCode, token);
            }
            T value = default;
            if (response.IsSuccessStatusCode && !string.IsNullOrWhiteSpace(body))
            {
                // 空 body 归 null（调用方已有 null 防线）；非 JSON body 翻译为 InvalidOperationException。
                try
                {
                    value = JsonSerializer.Deserialize<T>(body);
                }
                catch (JsonException ex)
                {
                    throw new InvalidOperationException($"{response.StatusCode}: {ex.Message}", ex);
                }
            }
            return new CommunityApiResponse<T>
            {
                IsSuccessStatusCode = response.IsSuccessStatusCode,
                StatusCode = response.StatusCode,
                Body = body,
                Value = value
            };
        }

        // 带 Bearer 的请求收到 401/403 说明 token 已失效：广播给订阅方统一登出+提示。
        private static void NotifyIfUnauthorized(HttpStatusCode statusCode, string token)
        {
            if (!string.IsNullOrWhiteSpace(token) && CommunityUnauthorizedNotifier.IsUnauthorized(statusCode))
            {
                CommunityUnauthorizedNotifier.Notify(token);
            }
        }

        // 网关大 HTML 错误页不截断会撑爆弹窗/日志。
        internal static string TruncateBody(string body)
        {
            return body.Length > 512 ? body[..512] + "..." : body;
        }
    }

    internal sealed class CommunityApiResponse<T>
    {
        public bool IsSuccessStatusCode { get; set; }
        public HttpStatusCode StatusCode { get; set; }
        public string Body { get; set; }
        public T Value { get; set; }

        // 非成功状态下的统一错误摘要（"状态码: 响应体"），调用方直接展示/入日志。
        public string ErrorSummary => IsSuccessStatusCode ? null : $"{StatusCode}: {CommunityApiClient.TruncateBody(Body)}";
    }
}
