using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Engine;

namespace Game
{
    // 帖子列表查询：社区列表页、用户主页帖子与下载趋势端点。
    internal static partial class CommunityManager
    {
        public static Task<CommunityApiResponse<PostsResponse>> LoadCommunityPostsAsync(
            CommunityApiClient apiClient,
            string apiBaseUrl,
            string token,
            int page,
            CommunityFileType fileType,
            CommunityOrderType orderType,
            string searchTitle,
            bool ignoreOrder,
            bool ignoreFileType,
            int? gameVersionId,
            CancellationToken cancellationToken = default)
        {
            List<string> queryParts = PostQueryBuilder.BuildCommunityQueryParts(
                page,
                fileType,
                orderType,
                searchTitle,
                ignoreOrder,
                ignoreFileType);
            if (gameVersionId.HasValue)
            {
                queryParts.Add($"gameVersionIds={gameVersionId.Value}");
            }

            return apiClient.GetJsonResultAsync<PostsResponse>(
                $"{apiBaseUrl}/post/list?{string.Join("&", queryParts)}",
                token,
                cancellationToken);
        }

        public static Task<CommunityApiResponse<PostsResponse>> GetUserPostsAsync(
            CommunityApiClient apiClient,
            string apiBaseUrl,
            string token,
            int userId,
            int page,
            int limit,
            CommunityFileType fileType,
            CancellationToken cancellationToken = default)
        {
            return apiClient.GetJsonResultAsync<PostsResponse>(
                $"{apiBaseUrl}/user/posts?userId={userId}&type=2&page={Math.Max(page, 1)}&limit={limit}&fileTypes={(int)fileType}",
                token,
                cancellationToken);
        }

        public static async Task<(PostDownloadTrendResponse Stats, string Error)> GetPostDownloadTrendAsync(
            CommunityApiClient apiClient,
            string apiBaseUrl,
            int postId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                CommunityApiResponse<PostDownloadTrendResponse> result =
                    await apiClient.GetJsonResultAsync<PostDownloadTrendResponse>(
                        $"{apiBaseUrl}/public-stats/post-downloads/trend?postId={postId}&granularity=day",
                        null,
                        cancellationToken);
                if (!result.IsSuccessStatusCode)
                {
                    return (null, result.ErrorSummary);
                }

                // 端点历史上有两种响应体：直接的 {post,...} 与 {code,data,msg} 包裹。
                // 以顶层是否含 "code" 属性区分，避免直接格式的空 post 被误读为包裹错误码；
                // 包裹非成功码显式报错，不再被"暂无数据"文案隐藏失败。
                PostDownloadTrendResponse stats = result.Value;
                if (stats?.Post == null && HasTopLevelCodeProperty(result.Body))
                {
                    PostDownloadTrendApiResponse wrapped =
                        JsonSerializer.Deserialize<PostDownloadTrendApiResponse>(result.Body);
                    if (wrapped != null && wrapped.Code != 200)
                    {
                        return (null, string.IsNullOrWhiteSpace(wrapped.Msg)
                            ? string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("CommunityManager", "DownloadTrendsResponseMalformed"), wrapped.Code)
                            : wrapped.Msg);
                    }

                    stats = wrapped?.Data;
                }
                return stats?.Post == null
                    ? (null, LanguageControl.Get("CommunityManager", "NoStats"))
                    : (stats, null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // 用户取消：传播；超时 OCE 不满足 when，仍按错误结果返回。
                throw;
            }
            catch (Exception ex)
            {
                return (null, ex.Message);
            }
        }

        private static bool HasTopLevelCodeProperty(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return false;
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(body);
                return document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty("code", out _);
            }
            catch (JsonException)
            {
                return false;
            }
        }

        /// <summary>帖子列表页装配：按需拉版本目录、匹配当前版本过滤 id，再查列表。</summary>
        public static async Task<CommunityPostsLoad> LoadPostsPageAsync(
            CommunityApiClient apiClient,
            CommunityPostsLoadRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            try
            {
                VersionListItem[] versionCatalog = request.CachedCatalog;
                Dictionary<int, string> gameVersionsById = request.CachedVersionMap;
                if ((request.LoadVersionCatalog || request.FilterToCurrentApiVersion)
                    && (versionCatalog == null || versionCatalog.Length == 0))
                {
                    VersionListResponse response = await LoadVersionCatalogAsync(apiClient, request.ApiBaseUrl, request.Token);
                    if (response?.Code == 200 && response.Data != null)
                    {
                        versionCatalog = response.Data;
                        gameVersionsById = VersionCatalogCache.BuildGameVersionNameMap(versionCatalog);
                    }
                }
                else if (versionCatalog != null && (gameVersionsById == null || gameVersionsById.Count == 0))
                {
                    gameVersionsById = VersionCatalogCache.BuildGameVersionNameMap(versionCatalog);
                }

                int? matchedVersionId = null;
                if (request.FilterToCurrentApiVersion)
                {
                    matchedVersionId = CommunityModVersionSelector.FindBestMatch(versionCatalog, request.CurrentApiVersion)?.Id;
                    if (!matchedVersionId.HasValue)
                    {
                        // 新目录已判定无匹配时不得回退过期缓存 id；仅当没有可用目录时才信任缓存。
                        bool hasFreshCatalog = versionCatalog != null && versionCatalog.Length > 0;
                        if (!hasFreshCatalog)
                        {
                            matchedVersionId = request.CachedPluginVersionId;
                        }
                        if (!matchedVersionId.HasValue && request.ResolvePluginVersionIdAsync != null)
                        {
                            matchedVersionId = await request.ResolvePluginVersionIdAsync();
                        }
                    }
                }

                CommunityApiResponse<PostsResponse> apiResult = await LoadCommunityPostsAsync(
                    apiClient,
                    request.ApiBaseUrl,
                    request.Token,
                    request.Page,
                    request.FileType,
                    request.OrderType,
                    request.SearchTitle,
                    request.IgnoreOrder,
                    request.IgnoreFileType,
                    matchedVersionId);
                return new CommunityPostsLoad
                {
                    Response = apiResult.Value,
                    VersionCatalog = versionCatalog,
                    GameVersionsById = gameVersionsById,
                    Error = apiResult.ErrorSummary
                };
            }
            catch (Exception ex)
            {
                return new CommunityPostsLoad { Error = ex.Message };
            }
        }

        /// <summary>用户主页帖子页装配：按需拉版本目录后查该用户的帖子列表。</summary>
        public static async Task<CommunityUserPostsLoad> LoadUserPostsPageAsync(
            CommunityApiClient apiClient,
            string apiBaseUrl,
            string token,
            int userId,
            int page,
            int limit,
            CommunityFileType fileType,
            bool loadVersionCatalog,
            VersionListItem[] cachedCatalog,
            Dictionary<int, string> cachedVersionMap)
        {
            try
            {
                VersionListItem[] versionCatalog = cachedCatalog;
                Dictionary<int, string> gameVersionsById = cachedVersionMap;
                if (loadVersionCatalog && (versionCatalog == null || versionCatalog.Length == 0))
                {
                    VersionListResponse response = await LoadVersionCatalogAsync(apiClient, apiBaseUrl, token);
                    if (response?.Code == 200 && response.Data != null)
                    {
                        versionCatalog = response.Data;
                        gameVersionsById = VersionCatalogCache.BuildGameVersionNameMap(versionCatalog);
                    }
                }
                else if (versionCatalog != null && (gameVersionsById == null || gameVersionsById.Count == 0))
                {
                    gameVersionsById = VersionCatalogCache.BuildGameVersionNameMap(versionCatalog);
                }

                CommunityApiResponse<PostsResponse> apiResult = await GetUserPostsAsync(
                    apiClient, apiBaseUrl, token, userId, page, limit, fileType);
                return new CommunityUserPostsLoad
                {
                    Response = apiResult.Value,
                    VersionCatalog = versionCatalog,
                    GameVersionsById = gameVersionsById,
                    Error = apiResult.ErrorSummary
                };
            }
            catch (Exception ex)
            {
                return new CommunityUserPostsLoad { Error = ex.Message };
            }
        }
    }

    internal sealed class CommunityPostsLoadRequest
    {
        public string ApiBaseUrl { get; set; }
        public string Token { get; set; }
        public int Page { get; set; }
        public CommunityFileType FileType { get; set; }
        public CommunityOrderType OrderType { get; set; }
        public string SearchTitle { get; set; }
        public bool IgnoreOrder { get; set; }
        public bool IgnoreFileType { get; set; }
        public bool LoadVersionCatalog { get; set; }
        public bool FilterToCurrentApiVersion { get; set; }
        public string CurrentApiVersion { get; set; }
        public VersionListItem[] CachedCatalog { get; set; }
        public Dictionary<int, string> CachedVersionMap { get; set; }
        public int? CachedPluginVersionId { get; set; }
        public Func<Task<int?>> ResolvePluginVersionIdAsync { get; set; }
    }

    internal sealed class CommunityPostsLoad
    {
        public PostsResponse Response { get; set; }
        public VersionListItem[] VersionCatalog { get; set; }
        public Dictionary<int, string> GameVersionsById { get; set; }
        public string Error { get; set; }
    }

    internal sealed class CommunityUserPostsLoad
    {
        public PostsResponse Response { get; set; }
        public VersionListItem[] VersionCatalog { get; set; }
        public Dictionary<int, string> GameVersionsById { get; set; }
        public string Error { get; set; }
    }
}
