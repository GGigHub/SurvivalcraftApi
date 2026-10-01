using System.Globalization;

namespace Game {
    // 本地模组已发布版本列表装载：解析帖子 → 归一化版本元数据 → 排序展示。
    sealed partial class LocalModNetworkManager {
        /// <summary>解析本地模组对应的已发布帖子并装载其版本历史（含游戏版本名映射）。</summary>
        internal static async Task<LocalModVersionsLoadResult> LoadModVersionsAsync(LocalModPostResolver postResolver,
            LocalModVersionsRequest request,
            CancellationToken cancellationToken = default) {
            if (request == null) {
                throw new ArgumentNullException(nameof(request));
            }
            try {
                int postId = request.CachedPostId > 0
                    ? request.CachedPostId
                    : await postResolver.ResolvePostIdAsync(
                        request.ApiBaseUrl,
                        request.Token,
                        request.FileName,
                        request.PackageName,
                        cancellationToken
                    );
                if (postId <= 0) {
                    return CreateVersionsErrorResult(
                        request,
                        string.Format(
                            CultureInfo.InvariantCulture,
                            LanguageControl.Get("LocalModNetworkManager", "CannotFindPublishedPostByFileName"),
                            request.FileName
                        )
                    );
                }
                PostDetailResponse postResponse = await postResolver.LoadPostDetailAsync(
                    request.ApiBaseUrl,
                    request.Token,
                    postId,
                    cancellationToken
                );
                if (postResponse?.Code != 200
                    || postResponse.Data == null) {
                    return CreateVersionsErrorResult(
                        request,
                        postResponse?.Msg ?? LanguageControl.Get("LocalModNetworkManager", "PostDetailsIsEmpty")
                    );
                }
                VersionListResponse versionListResponse = await postResolver.LoadVersionCatalogAsync(
                    request.ApiBaseUrl,
                    request.Token,
                    cancellationToken
                );
                if (versionListResponse?.Code != 200
                    || versionListResponse.Data == null) {
                    return CreateVersionsErrorResult(
                        request,
                        versionListResponse?.Msg ?? LanguageControl.Get("LocalModNetworkManager", "VersionListIsEmpty")
                    );
                }
                List<PostVersionItem> versions = PrepareVersionsForDisplay(postResponse.Data);
                versions.Sort((left, right) => ModUpdateSelector.CompareVersions(right, left));
                return new LocalModVersionsLoadResult {
                    RequestId = request.RequestId,
                    ResourceKey = request.ResourceKey,
                    ResourceDisplayName = request.ResourceDisplayName,
                    SourceFileName = request.FileName,
                    PostId = postId,
                    PostTitle = postResponse.Data.Title,
                    CurrentVersion = request.CurrentVersion,
                    CurrentApiVersion = request.CurrentApiVersion,
                    CurrentGameVersion = request.CurrentGameVersion,
                    Versions = versions,
                    GameVersionsById = VersionCatalogCache.BuildGameVersionNameMap(versionListResponse.Data),
                    VersionCatalog = versionListResponse.Data
                };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                // 用户取消：传播，不上报为加载错误。
                throw;
            }
            catch (Exception ex) {
                return CreateVersionsErrorResult(request, ex.Message);
            }
        }

        /// <summary>帖子版本与文件的继承元数据回填（postId/fileType 缺省时取上层值）。</summary>
        internal static List<PostVersionItem> PrepareVersionsForDisplay(PostItem post) {
            List<PostVersionItem> versions = [];
            foreach (PostVersionItem version in post?.PostVersions ?? []) {
                if (version == null) {
                    continue;
                }
                if (version.PostId <= 0) {
                    version.PostId = post.Id;
                }
                foreach (PostFileItem file in version.Files ?? []) {
                    if (file == null) {
                        continue;
                    }
                    if (file.PostId <= 0) {
                        file.PostId = version.PostId > 0 ? version.PostId : post.Id;
                    }
                    if (file.Type <= 0) {
                        file.Type = version.FileType > 0 ? version.FileType : post.FileType;
                    }
                }
                versions.Add(version);
            }
            return versions;
        }

        static LocalModVersionsLoadResult CreateVersionsErrorResult(LocalModVersionsRequest request, string error) =>
            new() { RequestId = request.RequestId, ResourceKey = request.ResourceKey, Error = error };
    }

    sealed class LocalModVersionsRequest : IRequestIdRequest {
        public int RequestId { get; set; }
        public string ResourceKey { get; set; }
        public string ResourceDisplayName { get; set; }
        public int CachedPostId { get; set; }
        public string ApiBaseUrl { get; set; }
        public string Token { get; set; }
        public string FileName { get; set; }
        public string PackageName { get; set; }
        public string CurrentVersion { get; set; }
        public string CurrentApiVersion { get; set; }
        public string CurrentGameVersion { get; set; }
    }

    sealed class LocalModVersionsLoadResult {
        public int RequestId { get; set; }
        public string ResourceKey { get; set; }
        public string ResourceDisplayName { get; set; }
        public string SourceFileName { get; set; }
        public int PostId { get; set; }
        public string PostTitle { get; set; }
        public string CurrentVersion { get; set; }
        public string CurrentApiVersion { get; set; }
        public string CurrentGameVersion { get; set; }
        public List<PostVersionItem> Versions { get; set; }
        public Dictionary<int, string> GameVersionsById { get; set; }
        public VersionListItem[] VersionCatalog { get; set; }
        public string Error { get; set; }
    }
}