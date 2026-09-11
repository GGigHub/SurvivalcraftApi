using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Versioning;

namespace Game
{
    // 自更新检查（模组自身资源在社区的最新版本）。移植日整链删除。
    internal static partial class CommunityManager
    {
        public static async Task<UpdateCheckResult> CheckSelfUpdateAsync(
            CommunityApiClient apiClient,
            ModCatalogClient catalogClient,
            LocalModPostResolver postResolver,
            string apiBaseUrl,
            string currentVersion,
            int requestId = 0,
            CancellationToken cancellationToken = default)
        {
            string normalizedApiBaseUrl = (apiBaseUrl ?? string.Empty).TrimEnd('/');
            UploadModsResponse uploadModsResponse = await catalogClient.FindByPackageNameAsync(
                normalizedApiBaseUrl,
                null,
                ModIdentity.PackageName,
                cancellationToken: cancellationToken);
            if (uploadModsResponse?.Code != 200)
            {
                throw new InvalidOperationException(uploadModsResponse?.Msg ?? LanguageControl.Get("ScbbsPlusCommunityScreen", "Upload", "Update", "ScbbsPlusResourceLookupByPackageFailed"));
            }

            (int postId, Exception resolveFailure) =
                await ResolveSelfUpdatePostIdAsync(postResolver, normalizedApiBaseUrl, uploadModsResponse, cancellationToken);
            if (postId <= 0)
            {
                throw new InvalidOperationException(
                    resolveFailure != null
                        ? string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusCommunityScreen", "Upload", "Update", "ResolveCommunityPostFailed"), resolveFailure.Message)
                        : LanguageControl.Get("ScbbsPlusCommunityScreen", "Upload", "Update", "NoPublishedCommunityPost"));
            }

            PostDetailResponse postResponse = await apiClient.GetJsonAsync<PostDetailResponse>(
                $"{normalizedApiBaseUrl}/post/detail?id={postId}",
                null,
                cancellationToken);
            if (postResponse?.Code != 200 || postResponse.Data == null)
            {
                throw new InvalidOperationException(postResponse?.Msg ?? LanguageControl.Get("ScbbsPlusCommunityScreen", "Upload", "Update", "PostDetailsIsEmpty"));
            }

            PostVersionItem latestVersionItem = ModUpdateSelector.FindLatestVersion(postResponse.Data.PostVersions);
            if (latestVersionItem == null)
            {
                throw new InvalidOperationException(LanguageControl.Get("ScbbsPlusCommunityScreen", "Upload", "Update", "NoUsableModVersionInPost"));
            }

            latestVersionItem.PostId = latestVersionItem.PostId > 0 ? latestVersionItem.PostId : postResponse.Data.Id > 0 ? postResponse.Data.Id : postId;
            PostFileItem file = ModUpdateSelector.FindBestFile(latestVersionItem);
            if (file == null || string.IsNullOrWhiteSpace(file.Url))
            {
                throw new InvalidOperationException(LanguageControl.Get("ScbbsPlusCommunityScreen", "Upload", "Update", "NoScmodFileInLatestVersion"));
            }

            file.PostId = file.PostId > 0 ? file.PostId : latestVersionItem.PostId;
            file.Type = file.Type > 0 ? file.Type : latestVersionItem.FileType > 0 ? latestVersionItem.FileType : postResponse.Data.FileType;
            string latestVersion = ExternalVersionParser.Normalize(latestVersionItem.Version);
            UpdateCheckResult result = new()
            {
                RequestId = requestId,
                CurrentVersion = currentVersion,
                LatestVersion = latestVersion,
                CommunityPost = postResponse.Data,
                CommunityVersion = latestVersionItem,
                CommunityFile = file
            };
            result.HasUpdate = ModUpdateSelector.IsNewer(latestVersionItem.NuGetVersion, result.CurrentNuGetVersion);
            return result;
        }

        private static async Task<(int PostId, Exception FirstFailure)> ResolveSelfUpdatePostIdAsync(
            LocalModPostResolver postResolver,
            string apiBaseUrl,
            UploadModsResponse response,
            CancellationToken cancellationToken)
        {
            Exception firstFailure = null;
            foreach (PostFileItem file in response?.Data?.List ?? [])
            {
                if (file == null)
                {
                    continue;
                }
                if (file.PostId > 0)
                {
                    return (file.PostId, null);
                }
                if (!string.IsNullOrWhiteSpace(file.Filename))
                {
                    try
                    {
                        // 复用 LocalModPostResolver 的「按文件名解析已发布帖子」（R2 收敛）：
                        // 同一 URL、同样 200 判定、同样"精确匹配优先唯一结果兜底"。
                        // 语义对齐差异：resolver 要求 published=true（旧实现忽略该标志，
                        // 下架/待审帖会被误绑 postId）。自更新查自己的帖子，理应 published。
                        int postId = await postResolver.ResolvePublishedPostIdByFileNameAsync(
                            apiBaseUrl,
                            token: null,
                            file.Filename,
                            cancellationToken);
                        if (postId > 0)
                        {
                            return (postId, null);
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                    {
                        // 单文件 4xx/网络故障不中断整次检查，继续尝试下一文件；
                        // 保留首个失败原因，全部失败时供调用方诊断。用户取消仍传播。
                        firstFailure ??= ex;
                    }
                }
            }
            return (0, firstFailure);
        }

        /// <summary>仓库 release 来源的自更新检查：取最新 release 并比较版本；网络/解析异常落为 Error 结果。</summary>
        public static async Task<UpdateCheckResult> CheckSelfReleaseUpdateAsync(
            RepositoryReleaseClient releaseClient,
            string releaseApiUrl,
            string displayName,
            string providerName,
            string currentVersion,
            int requestId = 0,
            CancellationToken cancellationToken = default)
        {
            UpdateCheckResult result = new()
            {
                RequestId = requestId,
                CurrentVersion = currentVersion
            };

            try
            {
                GiteeReleaseResponse release = await releaseClient.FetchLatestAsync(
                    releaseApiUrl,
                    displayName,
                    providerName,
                    cancellationToken);
                string latestVersion = ExternalVersionParser.Normalize(release.TagName ?? release.Name);
                result.LatestVersion = latestVersion;
                result.Release = release;
                result.ScmodAsset = RepositoryReleaseRules.FindScmodAsset(release);
                result.HasUpdate = result.LatestNuGetVersion != null
                    && (result.CurrentNuGetVersion == null
                        || VersionComparer.VersionRelease.Compare(result.LatestNuGetVersion, result.CurrentNuGetVersion) > 0);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // 用户取消：传播。
                throw;
            }
            catch (OperationCanceledException ex)
            {
                // HttpClient 超时抛出的 OCE：调用方未取消，按错误结果返回。
                result.Error = ex.Message;
            }
            catch (HttpRequestException ex)
            {
                result.Error = ex.Message;
            }
            catch (JsonException ex)
            {
                result.Error = ex.Message;
            }
            catch (InvalidOperationException ex)
            {
                // release 不存在 / 非成功状态码 / 数据为空。
                result.Error = ex.Message;
            }

            return result;
        }
    }
}
