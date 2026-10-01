namespace Game {
    // 本地模组更新检查来源：社区帖子（经 resolver）；仓库 release 回退链已随自更新删除令裁掉。
    sealed partial class LocalModNetworkManager {
        /// <summary>社区来源检查：解析 postId → 帖子详情 → 按兼容谓词选最新可用版本。</summary>
        internal static async Task<LocalModUpdateCheckResult> CheckCommunityUpdateAsync(LocalModPostResolver postResolver,
            LocalModCommunityUpdateRequest request,
            CancellationToken cancellationToken = default) {
            if (request == null) {
                throw new ArgumentNullException(nameof(request));
            }
            LocalModUpdateCheckResult result = CreateCheckResult(request);
            int postId = request.CachedPostId > 0
                ? request.CachedPostId
                : await postResolver.ResolvePostIdAsync(request.ApiBaseUrl, request.Token, request.FileName, request.PackageName, cancellationToken);
            if (postId <= 0) {
                return result;
            }
            PostDetailResponse response = await postResolver.LoadPostDetailAsync(request.ApiBaseUrl, request.Token, postId, cancellationToken);
            if (response?.Code != 200
                || response.Data == null) {
                result.Error = response?.Msg ?? LanguageControl.Get("LocalModNetworkManager", "PostDetailsIsEmpty");
                return result;
            }
            Func<PostVersionItem, bool> isCompatibleVersion = request.IsCompatibleVersion;
            if (isCompatibleVersion == null
                && request.LoadCompatibilityPredicateAsync != null) {
                isCompatibleVersion = await request.LoadCompatibilityPredicateAsync();
            }
            PostVersionItem latestVersion = ModUpdateSelector.FindLatestVersion(
                response.Data.PostVersions,
                request.PackageName,
                request.FileName,
                isCompatibleVersion
            );
            if (latestVersion == null) {
                PostVersionItem latestAnyVersion = ModUpdateSelector.FindLatestVersion(
                    response.Data.PostVersions,
                    request.PackageName,
                    request.FileName
                );
                result.PostId = postId;
                result.PostTitle = response.Data.Title;
                result.LatestVersion = ExternalVersionParser.Normalize(latestAnyVersion?.Version);
                result.NoUpdateReason = LanguageControl.Get("LocalModNetworkManager", "NoModVersionForCurrentGame");
                return result;
            }
            latestVersion.PostId = latestVersion.PostId > 0 ? latestVersion.PostId :
                response.Data.Id > 0 ? response.Data.Id : postId;
            string latestVersionText = ExternalVersionParser.Normalize(latestVersion.Version);
            result.PostId = latestVersion.PostId;
            result.PostTitle = response.Data.Title;
            result.LatestVersion = latestVersionText;
            result.LatestVersionItem = latestVersion;
            result.HasUpdate = ModUpdateSelector.IsNewer(latestVersion.NuGetVersion, result.CurrentNuGetVersion);
            PostFileItem file = ModUpdateSelector.FindBestFile(latestVersion, request.PackageName, request.FileName);
            if (file == null
                || string.IsNullOrWhiteSpace(file.Url)) {
                result.Error = LanguageControl.Get("LocalModNetworkManager", "NoScmodFileInLatestVersion");
                return result;
            }
            file.PostId = file.PostId > 0 ? file.PostId : latestVersion.PostId;
            file.Type = file.Type > 0 ? file.Type :
                latestVersion.FileType > 0 ? latestVersion.FileType : response.Data.FileType;
            result.File = file;
            return result;
        }

        // （游戏侧裁定）仓库 release 来源检查（CheckRepositoryUpdateAsync）随自更新删除令
        // 4126a14e 的 RepositoryRelease* 处置一并裁掉：游戏无 Gitee release 客户端，
        // 更新检查仅走社区帖源。LocalModRepositoryUpdateRequest DTO 保留于已移植模型备用。

        static LocalModUpdateCheckResult CreateCheckResult(LocalModCommunityUpdateRequest request) => new() {
            RequestId = request.RequestId,
            ResourceKey = request.ResourceKey,
            ResourceDisplayName = request.ResourceDisplayName,
            SourceFileName = request.FileName,
            CurrentVersion = request.CurrentVersion
        };
    }
}