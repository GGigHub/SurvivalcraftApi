using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Game
{
    internal sealed class LocalModPostResolver
    {
        /// <summary>
        /// 生产实例（本地模组社区帖组合点解散后的唯一接线），走社区域生产客户端；
        /// 测试自建实例注入 HTTP 桩。初始化会先触发 CommunityManager 静态构造，无环。
        /// </summary>
        internal static readonly LocalModPostResolver Instance =
            new(CommunityManager.ApiClient, CommunityManager.ModCatalog);

        private readonly CommunityApiClient m_apiClient;
        private readonly CommunityModCatalogClient m_catalogClient;

        public LocalModPostResolver(
            CommunityApiClient apiClient,
            CommunityModCatalogClient catalogClient)
        {
            m_apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            m_catalogClient = catalogClient ?? throw new ArgumentNullException(nameof(catalogClient));
        }

        public Task<PostDetailResponse> LoadPostDetailAsync(
            string apiBaseUrl,
            string token,
            int postId,
            CancellationToken cancellationToken = default)
        {
            return m_apiClient.GetJsonAsync<PostDetailResponse>(
                $"{apiBaseUrl}/post/detail?id={postId}",
                token,
                cancellationToken);
        }

        public Task<VersionListResponse> LoadVersionCatalogAsync(
            string apiBaseUrl,
            string token,
            CancellationToken cancellationToken = default)
        {
            return m_apiClient.GetJsonAsync<VersionListResponse>(
                $"{apiBaseUrl}/version/list",
                token,
                cancellationToken);
        }

        public async Task<int> ResolvePostIdAsync(
            string apiBaseUrl,
            string token,
            string fileName,
            string packageName,
            CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrWhiteSpace(packageName))
            {
                UploadModsResponse uploadModsResponse = await m_catalogClient.FindByPackageNameAsync(
                    apiBaseUrl,
                    token,
                    packageName,
                    cancellationToken: cancellationToken);
                if (uploadModsResponse?.Code == 200)
                {
                    PostFileItem[] matchedFiles = uploadModsResponse.Data?.List ?? [];
                    PostFileItem exactFile = matchedFiles.FirstOrDefault(file => CommunityLocalModMatchingRules.MatchesFileName(file?.Filename, fileName));
                    if (exactFile?.PostId > 0)
                    {
                        return exactFile.PostId;
                    }

                    PostFileItem fileWithPostId = matchedFiles.FirstOrDefault(file => file?.PostId > 0);
                    if (fileWithPostId != null)
                    {
                        return fileWithPostId.PostId;
                    }

                    // 无直接 PostId：限流并发解析各文件名（原来串行 await N 次往返），
                    // WhenAll 保持原顺序，取首个解析成功者。
                    using SemaphoreSlim limiter = new(3);
                    int[] postIds = await Task.WhenAll(matchedFiles.Select(async file =>
                    {
                        if (string.IsNullOrWhiteSpace(file?.Filename))
                        {
                            return 0;
                        }
                        await limiter.WaitAsync(cancellationToken);
                        try
                        {
                            return await ResolvePublishedPostIdByFileNameAsync(
                                apiBaseUrl,
                                token,
                                file.Filename,
                                cancellationToken);
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            // 用户取消：不吞，整体取消传播。
                            throw;
                        }
                        catch (Exception)
                        {
                            // 单个文件查询失败按未解析（0）处理，不让单点失败丢弃其余文件的解析结果。
                            return 0;
                        }
                        finally
                        {
                            limiter.Release();
                        }
                    }));
                    foreach (int postIdFromFile in postIds)
                    {
                        if (postIdFromFile > 0)
                        {
                            return postIdFromFile;
                        }
                    }
                }
            }

            return await ResolvePublishedPostIdByFileNameAsync(apiBaseUrl, token, fileName, cancellationToken);
        }

        public async Task<int> ResolvePublishedPostIdByFileNameAsync(
            string apiBaseUrl,
            string token,
            string fileName,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return 0;
            }

            string url = $"{apiBaseUrl}/post/file/published?filename={Uri.EscapeDataString(fileName)}";
            CommunityApiResponse<FilePublishedResponse> apiResult =
                await m_apiClient.GetJsonResultAsync<FilePublishedResponse>(url, token, cancellationToken);
            if (!apiResult.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusCommunityScreen", "Local", "LocalMods", "Network", "FindPostByFileNameFailed"), apiResult.StatusCode, apiResult.Body));
            }

            FilePublishedResponse publishedResponse = apiResult.Value;
            if (publishedResponse?.Code != 200)
            {
                throw new InvalidOperationException(publishedResponse?.Msg ?? string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusCommunityScreen", "Local", "LocalMods", "Network", "FindPostByFileNameMalformed"), apiResult.Body));
            }
            if (publishedResponse.Data?.Published != true || publishedResponse.Data.Posts == null || publishedResponse.Data.Posts.Length == 0)
            {
                return 0;
            }

            FilePublishedPost exact = publishedResponse.Data.Posts.FirstOrDefault(post =>
                post?.File != null && CommunityLocalModMatchingRules.MatchesFileName(post.File.Filename, fileName));
            if (exact != null)
            {
                return exact.Id;
            }
            return publishedResponse.Data.Posts.Length == 1 ? publishedResponse.Data.Posts[0].Id : 0;
        }
    }
}
