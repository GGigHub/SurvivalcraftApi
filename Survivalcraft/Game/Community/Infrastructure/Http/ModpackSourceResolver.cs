using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Engine;

namespace Game
{
    internal sealed class ModpackSourceResolver : IModpackSourceResolver
    {
        private readonly HttpClient m_downloadClient;
        private readonly CommunityApiClient m_communityApiClient;

        public ModpackSourceResolver(
            HttpClient downloadClient,
            CommunityApiClient communityApiClient)
        {
            m_downloadClient = downloadClient ?? throw new ArgumentNullException(nameof(downloadClient));
            m_communityApiClient = communityApiClient ?? throw new ArgumentNullException(nameof(communityApiClient));
        }

        public async Task<(byte[] data, string downloadedFileName, string resolvedUrl, PostFileItem communityFile)> ResolveAsync(
            ModpackItem item,
            IReadOnlyDictionary<string, byte[]> embeddedFiles,
            string apiBaseUrl,
            string token,
            CancellationToken cancellationToken = default)
        {
            byte[] data = null;
            string downloadedFileName = null;
            string resolvedUrl = item?.Url?.Trim();
            PostFileItem communityFile = null;

            if (ModpackRules.IsHttpUrl(resolvedUrl))
            {
                Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusModpackSourceResolver", "ModpackItemUsesNetworkUrl"), resolvedUrl));
                try
                {
                    (data, downloadedFileName) = await DownloadBytesAsync(resolvedUrl, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // 用户取消：传播，不触发 fileId 兜底下载。
                    throw;
                }
                catch (Exception ex) when (!string.IsNullOrWhiteSpace(item?.FileId))
                {
                    Log.Warning(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusModpackSourceResolver", "ModpackItemUrlDownloadFailedFallingBackToFileId"), item.FileId, ex.Message));
                }
            }
            else if (!string.IsNullOrWhiteSpace(resolvedUrl)
                && ModpackRules.TryGetEmbeddedFile(embeddedFiles, resolvedUrl, out byte[] embeddedData))
            {
                data = embeddedData;
                Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusModpackSourceResolver", "ModpackItemUsesZipEntry"), resolvedUrl));
            }

            if (data != null || string.IsNullOrWhiteSpace(item?.FileId))
            {
                return (data, downloadedFileName, resolvedUrl, communityFile);
            }

            Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusModpackSourceResolver", "ModpackItemUrlMissingResolvingByFileId"), resolvedUrl, item.FileId));
            communityFile = await FetchFileInfoByIdAsync(apiBaseUrl, token, item.FileId, cancellationToken);
            ModpackRules.ApplyItemFileInfo(item, communityFile);
            resolvedUrl = communityFile?.Url;
            if (ModpackRules.IsHttpUrl(resolvedUrl))
            {
                (data, downloadedFileName) = await DownloadBytesAsync(resolvedUrl, cancellationToken);
            }

            return (data, downloadedFileName, resolvedUrl, communityFile);
        }

        public async Task<PostFileItem> FetchFileInfoByIdAsync(
            string apiBaseUrl,
            string token,
            string fileId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(fileId))
            {
                return null;
            }

            string url = $"{ApiUrls.NormalizeBaseUrl(apiBaseUrl)}/upload/info?id={Uri.EscapeDataString(fileId.Trim())}";
            CommunityApiResponse<FileInfoResponse> apiResult =
                await m_communityApiClient.GetJsonResultAsync<FileInfoResponse>(url, token, cancellationToken);
            if (!apiResult.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusCommunityScreen", "Local", "Modpacks", "Page", "GetFileInfoByFileIdFailed"), apiResult.StatusCode, apiResult.Body));
            }

            string body = apiResult.Body;
            FileInfoResponse wrapped = apiResult.Value;
            PostFileItem directFile = null;
            try
            {
                directFile = JsonSerializer.Deserialize<PostFileItem>(body);
            }
            catch (JsonException)
            {
                // 非包装格式时保持 null 走下方校验，其他异常不吞。
            }

            if (wrapped?.Data != null)
            {
                return wrapped.Data;
            }
            if (wrapped != null && wrapped.Code != 0 && wrapped.Code != 200)
            {
                throw new InvalidOperationException(wrapped.Msg ?? string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusCommunityScreen", "Local", "Modpacks", "Page", "GetFileInfoByFileIdMalformed"), body));
            }
            if (directFile == null || string.IsNullOrWhiteSpace(directFile.Url))
            {
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusCommunityScreen", "Local", "Modpacks", "Page", "FileIdReturnedNoDownloadLink"), fileId));
            }

            return directFile;
        }

        internal async Task<(byte[] data, string fileName)> DownloadBytesAsync(
            string url,
            CancellationToken cancellationToken = default)
        {
            // 默认 GetAsync 先缓冲整个响应体，叠加 30s 客户端超时，慢网下载较大整合包子项会触发
            // TaskCanceledException。照 HttpDownloader 改 ResponseHeadersRead：先拿响应头、
            // 再按块读体，超时只作用于头部阶段，体读取不再受 30s 总超时约束。
            // URL 来自第三方整合包清单：响应体大小上限（防内存耗尽）。
            using HttpRequestMessage request = new(HttpMethod.Get, url);
            using HttpResponseMessage response = await m_downloadClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            DownloadGuard.EnsurePayloadWithinLimit(response.Content.Headers.ContentLength);
            using Stream responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using MemoryStream memory = new();
            byte[] buffer = new byte[81920];
            long downloaded = 0;
            // 体读取空闲超时（与 HttpDownloader 一致）：HttpClient.Timeout 对
            // ResponseHeadersRead 只覆盖到响应头，而队列路径传入的 token 可能是 None，
            // 服务器 stall（连接保持但不再发数据）会让 ReadAsync 永久挂起，导入任务卡死
            // 且 IsBusy 永真。每块数据到达重置空闲计时。
            using CancellationTokenSource readTimeoutCts =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            readTimeoutCts.CancelAfter(HttpDownloader.ReadIdleTimeout);
            int read;
            while ((read = await HttpDownloader.ReadWithIdleTimeoutAsync(
                responseStream, buffer, readTimeoutCts, cancellationToken)) > 0)
            {
                readTimeoutCts.CancelAfter(HttpDownloader.ReadIdleTimeout);
                downloaded += read;
                DownloadGuard.EnsurePayloadWithinLimit(downloaded);
                memory.Write(buffer, 0, read);
            }
            byte[] data = memory.ToArray();
            string fileName = response.Content.Headers.ContentDisposition?.FileNameStar
                ?? response.Content.Headers.ContentDisposition?.FileName;
            fileName = fileName?.Trim().Trim('"');
            return (data, string.IsNullOrWhiteSpace(fileName) ? null : Storage.GetFileName(fileName));
        }
    }
}
