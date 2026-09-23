using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Engine;

namespace Game
{
    internal readonly record struct DownloadProgress(long BytesDownloaded, long? TotalBytes);

    /// <summary>
    /// 下载体读空闲超时：服务器 stall（连接保持但不再发数据）令 linked CTS 的 CancelAfter 触发，
    /// 此时外部 CT 并未请求取消。保持 is-a OperationCanceledException——现有
    /// catch (OCE) when (外部CT未取消→不命中) / 兜底 OCE 映射（stall→网络错误、用户取消→取消）
    /// 的分类结构全部不变，同时可用本类型与用户取消精确区分，避免日志/重构把 stall 误判为取消。
    /// </summary>
    internal sealed class CommunityReadIdleTimeoutException : OperationCanceledException
    {
        public CommunityReadIdleTimeoutException(Exception innerException)
            : base("The download stalled: no data was received within the read idle timeout.", innerException)
        {
        }
    }

    /// <summary>
    /// 负责 HTTP 下载、断点续传和临时文件读取，不包含 UI 文案或导入逻辑。
    /// </summary>
    internal sealed class CommunityHttpDownloader
    {
        // 体读取空闲超时：连续两块数据之间的最大间隔，超过视为服务器 stall。
        // internal 非 readonly：测试注入短超时验证挂起读被终止，测试后恢复原值。
        internal static TimeSpan ReadIdleTimeout = TimeSpan.FromSeconds(30);

        private readonly HttpClient m_httpClient;

        public CommunityHttpDownloader(HttpClient httpClient)
        {
            m_httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        public Task<MemoryStream> DownloadToMemoryAsync(
            string url,
            string resumeFilePath,
            CancellationToken cancellationToken,
            IProgress<DownloadProgress> progress = null,
            Action<string> serverFileName = null)
        {
            return string.IsNullOrWhiteSpace(resumeFilePath)
                ? DownloadFreshToMemoryAsync(url, cancellationToken, progress, serverFileName)
                : DownloadResumableToMemoryAsync(url, resumeFilePath, cancellationToken, progress, serverFileName);
        }

        // 服务器指定文件名（Content-Disposition）：浏览器「另存为」名的同款来源，
        // 动态下载端点（如 LocalSend /download?fileId=…）的 URL 本身不含扩展名时，
        // 这是文件名与类型的唯一可靠依据。仅在成功响应上报；续传缓存失效重试时
        // 整条链路可能回调多次，调用方须以幂等方式消费。服务器未提供则不回调。
        private static void ReportServerFileName(HttpResponseMessage response, Action<string> serverFileName)
        {
            if (serverFileName == null)
            {
                return;
            }
            System.Net.Http.Headers.ContentDispositionHeaderValue disposition = response.Content.Headers.ContentDisposition;
            string name = disposition?.FileNameStar;
            if (string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(disposition?.FileName))
            {
                // .NET 的 FileName（非 FileNameStar）不解码 %xx 转义：直接上报会把转义名一路带成落盘名；
                // 非法转义序列（如字面 "%"）按原值保留。
                try
                {
                    name = Uri.UnescapeDataString(disposition.FileName);
                }
                catch (UriFormatException)
                {
                    name = disposition.FileName;
                }
            }
            if (!string.IsNullOrWhiteSpace(name))
            {
                serverFileName(name.Trim('"'));
            }
        }

        private async Task<MemoryStream> DownloadFreshToMemoryAsync(
            string url,
            CancellationToken cancellationToken,
            IProgress<DownloadProgress> progress,
            Action<string> serverFileName)
        {
            using HttpRequestMessage request = new(HttpMethod.Get, url);
            using HttpResponseMessage response = await m_httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
            ReportServerFileName(response, serverFileName);

            long? totalLength = response.Content.Headers.ContentLength;
            CommunityDownloadGuard.EnsurePayloadWithinLimit(totalLength);
            progress?.Report(new DownloadProgress(0, totalLength));
            MemoryStream memory = totalLength is > 0 and <= int.MaxValue
                ? new MemoryStream((int)totalLength.Value)
                : new MemoryStream();
            byte[] buffer = new byte[81920];
            long downloaded = 0;
            using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using (CancellationTokenSource readTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                // 体读取空闲超时：服务器 stall（连接保持但不再发数据）时挂起在 ReadAsync，
                // 叠加 RemoteImport 等场景的 CancellationToken.None 会永久挂起。
                // 首块前先启动空闲计时（响应头到达后体迟迟不发同样算 stall），每块数据到达重置 30s。
                readTimeoutCts.CancelAfter(ReadIdleTimeout);
                int read;
                while ((read = await ReadWithIdleTimeoutAsync(stream, buffer, readTimeoutCts, cancellationToken)) > 0)
                {
                    readTimeoutCts.CancelAfter(ReadIdleTimeout);
                    downloaded += read;
                    CommunityDownloadGuard.EnsurePayloadWithinLimit(downloaded);
                    await memory.WriteAsync(buffer, 0, read, cancellationToken);
                    progress?.Report(new DownloadProgress(downloaded, totalLength));
                }
            }

            memory.Position = 0;
            return memory;
        }

        private async Task<MemoryStream> DownloadResumableToMemoryAsync(
            string url,
            string resumeFilePath,
            CancellationToken cancellationToken,
            IProgress<DownloadProgress> progress,
            Action<string> serverFileName,
            bool allowCacheReset = true)
        {
            string directory = Storage.GetDirectoryName(resumeFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Storage.CreateDirectory(directory);
            }
            long existingLength = GetFileLength(resumeFilePath);
            CommunityDownloadCacheFile.Metadata existingMetadata = null;
            if (existingLength > 0
                && (!CommunityDownloadCacheFile.TryReadMetadata(resumeFilePath, out existingMetadata)
                    || !UrlsMatch(existingMetadata.RequestUrl, url)
                    || !HasEntityValidator(existingMetadata)))
            {
                DeleteDownloadCache(resumeFilePath);
                existingLength = 0;
            }
            progress?.Report(new DownloadProgress(existingLength, null));

            using HttpRequestMessage request = new(HttpMethod.Get, url);
            if (existingLength > 0)
            {
                request.Headers.Range = new RangeHeaderValue(existingLength, null);
            }

            using HttpResponseMessage response = await m_httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && existingLength > 0)
            {
                // ContentRange.Length 是服务器认可的总长：等于缓存长度即缓存完整。
                // 放宽验证器要求（部分 CDN 在 416 时不回显验证器）——完整缓存不应因
                // 缺失验证器被删除后全量重下。
                if (response.Content.Headers.ContentRange?.Length == existingLength
                    && ResponseMatchesCache(existingMetadata, response, relaxMissingValidators: true))
                {
                    return await ReadDownloadTempFileAsync(resumeFilePath, cancellationToken);
                }

                DeleteDownloadCache(resumeFilePath);
                return await RestartAfterInvalidCacheAsync(
                    url,
                    resumeFilePath,
                    cancellationToken,
                    progress,
                    serverFileName,
                    allowCacheReset,
                    "The server rejected the resume range and the local cache is incomplete.");
            }
            await EnsureSuccessAsync(response, cancellationToken);
            ReportServerFileName(response, serverFileName);

            bool isPartial = response.StatusCode == HttpStatusCode.PartialContent;
            ContentRangeHeaderValue contentRange = response.Content.Headers.ContentRange;
            if (isPartial && !IsValidPartialRange(contentRange, existingLength))
            {
                DeleteDownloadCache(resumeFilePath);
                return await RestartAfterInvalidCacheAsync(
                    url,
                    resumeFilePath,
                    cancellationToken,
                    progress,
                    serverFileName,
                    allowCacheReset,
                    "The server returned an invalid Content-Range for the resume request.");
            }

            if (existingLength > 0 && !ResponseMatchesCache(existingMetadata, response, relaxMissingValidators: true))
            {
                DeleteDownloadCache(resumeFilePath);
                return await RestartAfterInvalidCacheAsync(
                    url,
                    resumeFilePath,
                    cancellationToken,
                    progress,
                    serverFileName,
                    allowCacheReset,
                    "The download source changed while resuming the cache.");
            }

            if (existingLength > 0 && !isPartial)
            {
                DeleteDownloadCache(resumeFilePath);
                existingLength = 0;
                progress?.Report(new DownloadProgress(0, null));
            }

            long? responseLength = response.Content.Headers.ContentLength;
            long? totalLength = response.Content.Headers.ContentRange?.Length
                ?? (responseLength.HasValue ? existingLength + responseLength.Value : null);
            CommunityDownloadGuard.EnsurePayloadWithinLimit(totalLength);
            OpenFileMode mode = isPartial && existingLength > 0
                ? OpenFileMode.CreateOrOpen
                : OpenFileMode.Create;
            long downloaded = existingLength;
            CommunityDownloadCacheFile.WriteMetadata(
                resumeFilePath,
                CreateMetadata(url, response, totalLength));
            byte[] buffer = new byte[81920];

            using (Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken))
            using (Stream file = Storage.OpenFile(resumeFilePath, mode))
            using (CancellationTokenSource readTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                if (mode == OpenFileMode.CreateOrOpen)
                {
                    // TODO: 等待游戏本体更新后换用 Storage.Append
                    file.Seek(0, SeekOrigin.End);
                }
                // 体读取空闲超时：首块前先启动空闲计时，每块数据到达重置 30s（与 Fresh 路径一致），防服务器 stall 永久挂起。
                readTimeoutCts.CancelAfter(ReadIdleTimeout);
                int read;
                while ((read = await ReadWithIdleTimeoutAsync(stream, buffer, readTimeoutCts, cancellationToken)) > 0)
                {
                    readTimeoutCts.CancelAfter(ReadIdleTimeout);
                    downloaded += read;
                    CommunityDownloadGuard.EnsurePayloadWithinLimit(downloaded);
                    await file.WriteAsync(buffer, 0, read, cancellationToken);
                    progress?.Report(new DownloadProgress(downloaded, totalLength));
                }
            }

            if (isPartial && !IsCompletePartialDownload(contentRange, existingLength, downloaded))
            {
                DeleteDownloadCache(resumeFilePath);
                return await RestartAfterInvalidCacheAsync(
                    url,
                    resumeFilePath,
                    cancellationToken,
                    progress,
                    serverFileName,
                    allowCacheReset,
                    "The server response body does not match its Content-Range.");
            }

            return await ReadDownloadTempFileAsync(resumeFilePath, cancellationToken);
        }

        private Task<MemoryStream> RestartAfterInvalidCacheAsync(
            string url,
            string resumeFilePath,
            CancellationToken cancellationToken,
            IProgress<DownloadProgress> progress,
            Action<string> serverFileName,
            bool allowCacheReset,
            string error)
        {
            if (!allowCacheReset)
            {
                throw new InvalidOperationException(error);
            }

            return DownloadResumableToMemoryAsync(
                url,
                resumeFilePath,
                cancellationToken,
                progress,
                serverFileName,
                allowCacheReset: false);
        }

        // 读一块响应体。linked CTS 触发取消而外部 CT 未请求取消 = 读空闲超时（服务器 stall），
        // 转抛 CommunityReadIdleTimeoutException（is-a OCE）与用户取消区分；外部 CT 已取消时
        // 原始 OCE 原样传播，用户取消路径行为不变。internal 供 ModpackSourceResolver（后续批次移植）
        // 的对称读空闲超时机制复用。
        internal static async Task<int> ReadWithIdleTimeoutAsync(
            Stream stream,
            byte[] buffer,
            CancellationTokenSource readTimeoutCts,
            CancellationToken cancellationToken)
        {
            try
            {
                return await stream.ReadAsync(buffer, 0, buffer.Length, readTimeoutCts.Token);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new CommunityReadIdleTimeoutException(ex);
            }
        }

        private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            if (response.IsSuccessStatusCode)
            {
                return;
            }

            string errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"{response.StatusCode}: {errorBody}");
        }

        private static async Task<MemoryStream> ReadDownloadTempFileAsync(
            string path,
            CancellationToken cancellationToken)
        {
            // 续传完成读回内存前校验既有缓存大小（下载过程已受限，此处防外部篡改的异常大文件）。
            long fileSize = 0;
            try
            {
                fileSize = Storage.FileExists(path) ? Storage.GetFileSize(path) : 0;
            }
            catch (Exception ex)
            {
                Log.Warning(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusHttpDownloader", "ReadCacheFileSizeFailed"), path, ex.Message));
            }
            CommunityDownloadGuard.EnsurePayloadWithinLimit(fileSize);

            using Stream source = Storage.OpenFile(path, OpenFileMode.Read);
            MemoryStream data = new();
            await source.CopyToAsync(data, 81920, cancellationToken);
            data.Position = 0;
            return data;
        }

        private static long GetFileLength(string path)
        {
            try
            {
                return Storage.FileExists(path) ? Storage.GetFileSize(path) : 0;
            }
            catch (Exception ex)
            {
                Log.Warning(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusHttpDownloader", "GetCacheFileSizeFailed"), path, ex.Message));
                return 0;
            }
        }

        private static CommunityDownloadCacheFile.Metadata CreateMetadata(
            string requestUrl,
            HttpResponseMessage response,
            long? totalLength)
        {
            return new CommunityDownloadCacheFile.Metadata
            {
                RequestUrl = requestUrl,
                ResponseUrl = response.RequestMessage?.RequestUri?.AbsoluteUri ?? requestUrl,
                ETag = response.Headers.ETag?.Tag,
                LastModified = response.Content.Headers.LastModified,
                TotalLength = totalLength
            };
        }

        private static bool ResponseMatchesCache(
            CommunityDownloadCacheFile.Metadata metadata,
            HttpResponseMessage response,
            bool relaxMissingValidators)
        {
            if (metadata == null)
            {
                return false;
            }

            bool hasStoredValidator = !string.IsNullOrWhiteSpace(metadata.ETag) || metadata.LastModified.HasValue;

            string eTag = response.Headers.ETag?.Tag;
            if (!string.IsNullOrWhiteSpace(metadata.ETag)
                && !string.IsNullOrWhiteSpace(eTag)
                && !string.Equals(metadata.ETag, eTag, StringComparison.Ordinal))
            {
                return false;
            }

            DateTimeOffset? lastModified = response.Content.Headers.LastModified;
            if (metadata.LastModified.HasValue
                && lastModified.HasValue
                && metadata.LastModified.Value != lastModified.Value)
            {
                return false;
            }

            long? responseLength = response.Content.Headers.ContentRange?.Length;
            if (metadata.TotalLength.HasValue
                && responseLength.HasValue
                && metadata.TotalLength.Value != responseLength.Value)
            {
                return false;
            }

            // 缓存有验证器而响应完全缺失验证器：416 来自源服务器，缺失更可疑，判败重启下载；
            // 206 部分 CDN 不回显验证器，仅在总长可比对且一致时放行，总长未知则保守判败。
            // 只有存储与响应"同类型"验证器（ETag 对 ETag、LastModified 对 LastModified）都存在
            // 才算可验证：类型错开（存 ETag 回 LastModified）说明源已变，放行会把响应追加到过期前缀。
            bool hasSharedValidator = (!string.IsNullOrWhiteSpace(metadata.ETag) && !string.IsNullOrWhiteSpace(eTag))
                || (metadata.LastModified.HasValue && lastModified.HasValue);
            return !hasStoredValidator
                || hasSharedValidator
                || (relaxMissingValidators && metadata.TotalLength.HasValue && responseLength.HasValue);
        }

        private static bool HasEntityValidator(CommunityDownloadCacheFile.Metadata metadata)
        {
            return metadata != null
                && (!string.IsNullOrWhiteSpace(metadata.ETag) || metadata.LastModified.HasValue);
        }

        private static bool IsValidPartialRange(ContentRangeHeaderValue range, long expectedStart)
        {
            return range?.From == expectedStart
                && range.To.HasValue
                && range.Length.HasValue
                && range.To.Value >= expectedStart
                && range.Length.Value > expectedStart
                && range.To.Value < range.Length.Value;
        }

        private static bool IsCompletePartialDownload(
            ContentRangeHeaderValue range,
            long existingLength,
            long downloadedLength)
        {
            return range != null
                && range.From.HasValue
                && range.To.HasValue
                && range.Length.HasValue
                && downloadedLength - existingLength == range.To.Value - range.From.Value + 1
                && downloadedLength == range.Length.Value;
        }

        private static bool UrlsMatch(string left, string right)
        {
            return !string.IsNullOrWhiteSpace(left)
                && !string.IsNullOrWhiteSpace(right)
                && string.Equals(left, right, StringComparison.Ordinal);
        }

        private static void DeleteDownloadCache(string path)
        {
            if (!CommunityDownloadCacheFile.TryDelete(path, out string error))
            {
                throw new IOException($"Failed to delete download cache: {error}");
            }
        }
    }
}
