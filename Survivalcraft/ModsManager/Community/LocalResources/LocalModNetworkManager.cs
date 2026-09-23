using System;
using Engine;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Game
{
    internal enum LocalModNetworkStartKind
    {
        Started,
        Busy,
        InvalidResource,
        NoDependencies
    }

    internal sealed class LocalModNetworkStartResult
    {
        public LocalModNetworkStartKind Kind { get; init; }
        public int RequestId { get; init; }
        public bool Started => Kind == LocalModNetworkStartKind.Started;
    }

    internal sealed class LocalModDownloadCommand
    {
        public string Url { get; init; }
        public string FileName { get; init; }
        public ExternalContentType ContentType { get; init; }
        public bool IsRepositoryReleaseAsset { get; init; }
        public CommunityDownloadSourceOption Source { get; init; }
        public CommunityDownloadFeedbackInfo Feedback { get; init; }
        public string ReplaceExistingModPackageName { get; init; }
        public string ReplaceExistingModPath { get; init; }
        public PostVersionItem VersionItem { get; init; }
        public bool IsValid => !string.IsNullOrWhiteSpace(Url) && !string.IsNullOrWhiteSpace(FileName);
    }

    internal sealed class LocalModUpdateCandidate
    {
        public string ResourceKey { get; init; }
        public string ResourceDisplayName { get; init; }
        public LocalResourceItem ResourceItem { get; init; }
        public LocalModUpdateCheckResult UpdateInfo { get; init; }
    }

    internal sealed partial class LocalModNetworkManager
    {
        private readonly TaskSlot<LocalModUpdateCheckResult> m_updateCheckSlot = new(LanguageControl.Get("ScbbsPlusTaskSlot", "UpdateCheck"));
        private readonly TaskSlot<LocalModAutoUpdateScanResult> m_autoUpdateScanSlot = new(LanguageControl.Get("ScbbsPlusTaskSlot", "AutoUpdateScan"));
        private readonly TaskSlot<LocalModVersionsLoadResult> m_versionsLoadSlot = new(LanguageControl.Get("ScbbsPlusTaskSlot", "VersionList"));
        private readonly TaskSlot<MissingModDependenciesLookupResult> m_dependencyLookupSlot = new(LanguageControl.Get("ScbbsPlusTaskSlot", "DependencyLookup"));
        private CancellationTokenSource m_cancellation;

        public bool IsUpdateCheckRunning => m_updateCheckSlot.IsRunning;
        public bool IsAutoUpdateScanRunning => m_autoUpdateScanSlot.IsRunning;
        public bool IsVersionsLoadRunning => m_versionsLoadSlot.IsRunning;
        public bool IsDependencyLookupRunning => m_dependencyLookupSlot.IsRunning;
        public int CurrentUpdateCheckRequestId => m_updateCheckSlot.RequestId;
        public int CurrentAutoUpdateScanRequestId => m_autoUpdateScanSlot.RequestId;
        public int CurrentVersionsLoadRequestId => m_versionsLoadSlot.RequestId;
        public int CurrentDependencyLookupRequestId => m_dependencyLookupSlot.RequestId;

        /// <summary>
        /// 四类网络任务共用的启动流程：忙拒绝 → 请求有效性守卫（各自语义经
        /// validate 返回失败 Kind）→ 单槽启动并回填 RequestId。
        /// </summary>
        private LocalModNetworkStartResult Start<TRequest, TResult>(
            TaskSlot<TResult> slot,
            TRequest request,
            Func<TRequest, LocalModNetworkStartKind> validate,
            Func<TRequest, int, CancellationToken, Task<TResult>> run)
            where TRequest : class, IRequestIdRequest
            where TResult : class
        {
            if (slot.IsRunning)
            {
                return new LocalModNetworkStartResult { Kind = LocalModNetworkStartKind.Busy };
            }
            LocalModNetworkStartKind invalidKind = validate(request);
            if (invalidKind != LocalModNetworkStartKind.Started)
            {
                return new LocalModNetworkStartResult { Kind = invalidKind };
            }

            int requestId = slot.Start((id, token) =>
            {
                request.RequestId = id;
                return run(request, id, token);
            }, (m_cancellation ??= new CancellationTokenSource()).Token);
            return new LocalModNetworkStartResult
            {
                Kind = LocalModNetworkStartKind.Started,
                RequestId = requestId
            };
        }

        private static LocalModNetworkStartKind ValidateNotNull<TRequest>(TRequest request)
            where TRequest : class
        {
            return request == null
                ? LocalModNetworkStartKind.InvalidResource
                : LocalModNetworkStartKind.Started;
        }

        public LocalModNetworkStartResult StartUpdateCheck(LocalModUpdateCheckRequest request)
        {
            return Start(m_updateCheckSlot, request, ValidateNotNull, RunUpdateCheck);
        }

        public bool TryTakeUpdateCheck(out LocalModUpdateCheckResult result)
        {
            return m_updateCheckSlot.TryTake(out result);
        }

        public LocalModNetworkStartResult StartAutoUpdateScan(LocalModAutoUpdateScanRequest request)
        {
            return Start(m_autoUpdateScanSlot, request, ValidateNotNull, RunAutoUpdateScan);
        }

        public bool TryTakeAutoUpdateScan(out LocalModAutoUpdateScanResult result)
        {
            return m_autoUpdateScanSlot.TryTake(out result);
        }

        public LocalModNetworkStartResult StartVersionsLoad(LocalModVersionsRequest request)
        {
            return Start(m_versionsLoadSlot, request, ValidateNotNull, RunVersionsLoad);
        }

        public bool TryTakeVersionsLoad(out LocalModVersionsLoadResult result)
        {
            return m_versionsLoadSlot.TryTake(out result);
        }

        public LocalModNetworkStartResult StartDependencyLookup(MissingModDependenciesLookupRequest request)
        {
            return Start(
                m_dependencyLookupSlot,
                request,
                req => req == null || req.Dependencies == null || req.Dependencies.Count == 0
                    ? LocalModNetworkStartKind.NoDependencies
                    : LocalModNetworkStartKind.Started,
                RunDependencyLookup);
        }

        public bool TryTakeDependencyLookup(out MissingModDependenciesLookupResult result)
        {
            return m_dependencyLookupSlot.TryTake(out result);
        }

        // 批量扫描的有界并发度（ModpackFileInfoManager 同款先例）；服务器友好取 4。
        private const int ScanConcurrency = 4;

        /// <summary>
        /// 更新检查编排：社区命中即返回；RequirePublishedPost
        /// 时把"找不到帖子"落为 Error。依赖经参数穿线（组合根静态字段在运行路径注入，
        /// 测试传 HTTP 桩），保持可注入性。
        /// （游戏侧裁定：仓库 release 回退链随自更新删除令移除，仅保留社区帖源——
        /// 与 4126a14e「删除 ScbbsPlus 自更新链」对 RepositoryRelease* 的处置一致。）
        /// </summary>
        internal static async Task<LocalModUpdateCheckResult> CheckUpdateAsync(
            LocalModUpdateCheckRequest request,
            CancellationToken cancellationToken,
            LocalModPostResolver postResolver)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            LocalModUpdateCheckResult communityResult = await CheckCommunityUpdateAsync(
                postResolver,
                CreateCommunityRequest(request, cancellationToken, postResolver),
                cancellationToken);
            if (communityResult.PostId > 0)
            {
                return communityResult;
            }

            if (request.RequirePublishedPost && string.IsNullOrWhiteSpace(communityResult.Error))
            {
                communityResult.Error = string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusCommunityScreen", "Local", "LocalMods", "Network", "CannotFindPublishedPostByFileName"), request.FileName);
            }
            return communityResult;
        }

        internal static async Task<LocalModAutoUpdateScanResult> ScanForUpdatesAsync(
            LocalModAutoUpdateScanRequest request,
            CancellationToken cancellationToken,
            LocalModPostResolver postResolver)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            LocalModCompatibilityContext compatibilityContext =
                await LoadCompatibilityContextAsync(request, cancellationToken, postResolver);
            IReadOnlyList<LocalModUpdateTarget> targets = (request.Targets ?? [])
                .Where(target => target != null)
                .ToArray();
            // 有界并发（原逐项串行 15 模组 6-12s → 2-4s）：结果按 targets 原序落位，
            // 单项失败不中断整批（与串行版逐项 catch 语义一致），用户取消中止整批。
            LocalModAutoUpdateScanEntry[] entries = new LocalModAutoUpdateScanEntry[targets.Count];
            using SemaphoreSlim gate = new(ScanConcurrency);
            Task[] workers = new Task[targets.Count];
            for (int i = 0; i < targets.Count; i++)
            {
                int index = i;
                LocalModUpdateTarget target = targets[i];
                workers[i] = Task.Run(async () =>
                {
                    await gate.WaitAsync(cancellationToken);
                    try
                    {
                        entries[index] = await EvaluateTargetAsync(request, target, compatibilityContext, cancellationToken, postResolver);
                    }
                    finally
                    {
                        gate.Release();
                    }
                }, cancellationToken);
            }

            try
            {
                await Task.WhenAll(workers);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // 用户取消：中止整批扫描；超时 OCE 不满足 when，仍按逐项失败记录。
                throw;
            }

            return new LocalModAutoUpdateScanResult
            {
                RequestId = request.RequestId,
                Signature = request.Signature,
                Entries = [.. entries]
            };
        }

        /// <summary>单项检查 + 结果落位（含逐项异常→失败 entry 的既有语义）。</summary>
        private static async Task<LocalModAutoUpdateScanEntry> EvaluateTargetAsync(
            LocalModAutoUpdateScanRequest request,
            LocalModUpdateTarget target,
            LocalModCompatibilityContext compatibilityContext,
            CancellationToken cancellationToken,
            LocalModPostResolver postResolver)
        {
            LocalModCommunityStatusInfo status = new() { CheckCompleted = true };
            LocalModUpdateCheckResult updateInfo = null;
            try
            {
                LocalModUpdateCheckResult result = await CheckUpdateAsync(new LocalModUpdateCheckRequest
                {
                    ResourceKey = target.ResourceKey,
                    ResourceDisplayName = target.DisplayName,
                    CachedPostId = target.CachedPostId,
                    ApiBaseUrl = request.ApiBaseUrl,
                    Token = request.Token,
                    FileName = target.FileName,
                    CurrentVersion = target.CurrentVersion,
                    PackageName = target.PackageName,
                    RepositoryUrl = target.RepositoryUrl,
                    CurrentApiVersion = request.CurrentApiVersion,
                    CurrentGameVersion = request.CurrentGameVersion,
                    VersionCatalog = compatibilityContext.VersionCatalog,
                    GameVersionsById = compatibilityContext.GameVersionsById
                }, cancellationToken, postResolver);
                if (result.PostId > 0)
                {
                    status.IsPublished = true;
                    status.PostId = result.PostId;
                }
                status.LatestVersion = result.LatestVersion;
                status.HasUpdate = result.HasUpdate;

                if (result?.HasUpdate == true && result.File != null)
                {
                    updateInfo = result;
                }
                else if (!string.IsNullOrWhiteSpace(result?.Error))
                {
                    Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusLocalModNetworkManager", "AutoUpdateCheckFailed"), target.DisplayName, result.Error));
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusLocalModNetworkManager", "AutoUpdateCheckFailed"), target.DisplayName, ex.Message));
            }

            return new LocalModAutoUpdateScanEntry
            {
                ResourceKey = target.ResourceKey,
                DisplayName = target.DisplayName,
                FileName = target.FileName,
                Status = status,
                UpdateInfo = updateInfo
            };
        }

        private static LocalModCommunityUpdateRequest CreateCommunityRequest(
            LocalModUpdateCheckRequest request,
            CancellationToken cancellationToken,
            LocalModPostResolver postResolver)
        {
            // 谓词可能在单次检查中被多次求值（多版本比对）：上下文加载结果缓存到本次请求
            // 生命周期，版本目录缺失时不重复请求（ScanAsync 已传目录，独立检查路径受益）。
            LocalModCompatibilityContext compatibilityContext = null;
            return new LocalModCommunityUpdateRequest
            {
                RequestId = request.RequestId,
                ResourceKey = request.ResourceKey,
                ResourceDisplayName = request.ResourceDisplayName,
                CachedPostId = request.CachedPostId,
                ApiBaseUrl = request.ApiBaseUrl,
                Token = request.Token,
                FileName = request.FileName,
                CurrentVersion = request.CurrentVersion,
                PackageName = request.PackageName,
                LoadCompatibilityPredicateAsync = async () =>
                {
                    compatibilityContext ??= await LoadCompatibilityContextAsync(request, cancellationToken, postResolver);
                    return version => ModCompatibility.IsCompatibleWithCurrentVersion(
                        version,
                        request.CurrentApiVersion,
                        request.CurrentGameVersion,
                        compatibilityContext.GameVersionsById,
                        compatibilityContext.VersionCatalog);
                }
            };
        }

        private static async Task<LocalModCompatibilityContext> LoadCompatibilityContextAsync(
            LocalModUpdateCheckRequest request,
            CancellationToken cancellationToken,
            LocalModPostResolver postResolver)
        {
            VersionListItem[] versionCatalog = request.VersionCatalog;
            Dictionary<int, string> gameVersionsById = request.GameVersionsById;
            if ((versionCatalog == null || versionCatalog.Length == 0)
                && !string.IsNullOrWhiteSpace(request.ApiBaseUrl))
            {
                VersionListResponse response = await postResolver.LoadVersionCatalogAsync(
                    request.ApiBaseUrl,
                    request.Token,
                    cancellationToken);
                if (response?.Code == 200 && response.Data != null)
                {
                    versionCatalog = response.Data;
                    gameVersionsById = VersionCatalogCache.BuildGameVersionNameMap(versionCatalog);
                }
            }

            return new LocalModCompatibilityContext
            {
                VersionCatalog = versionCatalog ?? [],
                GameVersionsById = gameVersionsById ?? new Dictionary<int, string>()
            };
        }

        private static async Task<LocalModCompatibilityContext> LoadCompatibilityContextAsync(
            LocalModAutoUpdateScanRequest request,
            CancellationToken cancellationToken,
            LocalModPostResolver postResolver)
        {
            return await LoadCompatibilityContextAsync(new LocalModUpdateCheckRequest
            {
                ApiBaseUrl = request.ApiBaseUrl,
                Token = request.Token,
                VersionCatalog = request.VersionCatalog,
                GameVersionsById = request.GameVersionsById
            }, cancellationToken, postResolver);
        }

        private static async Task<LocalModUpdateCheckResult> RunUpdateCheck(
            LocalModUpdateCheckRequest request, int requestId, CancellationToken token)
        {
            try
            {
                return await CheckUpdateAsync(
                    request,
                    token,
                    LocalModPostResolver.Instance);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return null;
            }
            catch (Exception ex)
            {
                return new LocalModUpdateCheckResult
                {
                    RequestId = requestId,
                    ResourceKey = request.ResourceKey,
                    CurrentVersion = request.CurrentVersion,
                    Error = ex.Message
                };
            }
        }

        private static async Task<LocalModAutoUpdateScanResult> RunAutoUpdateScan(
            LocalModAutoUpdateScanRequest request, int requestId, CancellationToken token)
        {
            try
            {
                return await ScanForUpdatesAsync(
                    request,
                    token,
                    LocalModPostResolver.Instance);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return null;
            }
            catch (Exception ex)
            {
                return new LocalModAutoUpdateScanResult
                {
                    RequestId = requestId,
                    Signature = request.Signature,
                    Error = ex.Message
                };
            }
        }

        private static async Task<LocalModVersionsLoadResult> RunVersionsLoad(
            LocalModVersionsRequest request, int requestId, CancellationToken token)
        {
            try
            {
                return await LoadModVersionsAsync(LocalModPostResolver.Instance, request, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return null;
            }
            catch (Exception ex)
            {
                return new LocalModVersionsLoadResult
                {
                    RequestId = requestId,
                    ResourceKey = request.ResourceKey,
                    Error = ex.Message
                };
            }
        }

        private static async Task<MissingModDependenciesLookupResult> RunDependencyLookup(
            MissingModDependenciesLookupRequest request, int requestId, CancellationToken token)
        {
            try
            {
                return await LookupMissingDependenciesAsync(request, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return null;
            }
            catch (Exception ex)
            {
                return new MissingModDependenciesLookupResult
                {
                    RequestId = requestId,
                    ResourceKey = request.ResourceKey,
                    IsWorldUsedModsLookup = request.IsWorldUsedModsLookup,
                    Error = ex.Message
                };
            }
        }

        private static async Task<MissingModDependenciesLookupResult> LookupMissingDependenciesAsync(
            MissingModDependenciesLookupRequest request, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            try
            {
                ModDependencyLookupData lookup = await LookupModDependenciesAsync(
                    CommunityManager.ModCatalog,
                    request.ApiBaseUrl,
                    request.Token,
                    request.Dependencies,
                    cancellationToken);
                return new MissingModDependenciesLookupResult
                {
                    RequestId = request.RequestId,
                    ResourceKey = request.ResourceKey,
                    ResourceDisplayName = request.ResourceDisplayName,
                    IsWorldUsedModsLookup = request.IsWorldUsedModsLookup,
                    Files = lookup.Files,
                    MissingDependencyLabels = request.IsWorldUsedModsLookup
                        ? BuildMissingDependencyLabels(lookup.MissingDependencies)
                        : null,
                    PreferredVersionsByPackage = lookup.PreferredVersionsByPackage
                };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // 用户取消：传播，不上报为查询错误。
                throw;
            }
            catch (Exception ex)
            {
                return new MissingModDependenciesLookupResult
                {
                    RequestId = request.RequestId,
                    ResourceKey = request.ResourceKey,
                    ResourceDisplayName = request.ResourceDisplayName,
                    IsWorldUsedModsLookup = request.IsWorldUsedModsLookup,
                    Error = ex.Message
                };
            }
        }

        internal static List<string> BuildMissingDependencyLabels(IReadOnlyList<ModDependencyInfo> dependencies)
        {
            List<string> labels = [];
            HashSet<string> addedPackages = new(StringComparer.OrdinalIgnoreCase);
            foreach (ModDependencyInfo dependency in dependencies ?? [])
            {
                string packageName = dependency?.PackageName?.Trim();
                if (string.IsNullOrWhiteSpace(packageName) || !addedPackages.Add(packageName))
                {
                    continue;
                }

                string name = string.IsNullOrWhiteSpace(dependency.DisplayName)
                    ? packageName
                    : $"{dependency.DisplayName} / {packageName}";
                labels.Add(string.IsNullOrWhiteSpace(dependency.VersionRangeString)
                    ? name
                    : $"{name} ({dependency.VersionRangeString})");
            }
            return labels;
        }

        public bool IsCurrentResult(string resultResourceKey, string currentResourceKey, int resultRequestId, int currentRequestId)
        {
            return resultRequestId == currentRequestId
                && !string.IsNullOrWhiteSpace(resultResourceKey)
                && string.Equals(resultResourceKey, currentResourceKey, StringComparison.OrdinalIgnoreCase);
        }

        public bool IsCurrentDependencyResult(
            MissingModDependenciesLookupResult result,
            string currentModResourceKey,
            string currentWorldResourceKey)
        {
            if (result == null)
            {
                return false;
            }
            string currentKey = result.IsWorldUsedModsLookup ? currentWorldResourceKey : currentModResourceKey;
            return IsCurrentResult(result.ResourceKey, currentKey, result.RequestId, m_dependencyLookupSlot.RequestId);
        }

        public IReadOnlyList<PostFileItem> FilterDownloadableFiles(IEnumerable<PostFileItem> files)
        {
            return (files ?? [])
                .Where(file => file != null && !string.IsNullOrWhiteSpace(file.Url))
                .ToArray();
        }

        public IReadOnlyList<LocalModUpdateCandidate> BuildUpdateCandidates(
            IEnumerable<LocalResourceItem> resources,
            IReadOnlyDictionary<string, LocalModUpdateCheckResult> updateDetails,
            Func<LocalResourceItem, string> getResourceKey,
            Func<LocalResourceItem, string> getDisplayName)
        {
            if (getResourceKey == null || getDisplayName == null)
            {
                throw new ArgumentNullException(getResourceKey == null ? nameof(getResourceKey) : nameof(getDisplayName));
            }

            List<LocalModUpdateCandidate> candidates = [];
            foreach (LocalResourceItem resource in resources ?? [])
            {
                if (resource?.Type != ExternalContentType.Mod)
                {
                    continue;
                }
                string key = getResourceKey(resource);
                if (string.IsNullOrWhiteSpace(key)
                    || updateDetails == null
                    || !updateDetails.TryGetValue(key, out LocalModUpdateCheckResult updateInfo)
                    || updateInfo == null
                    || !updateInfo.HasUpdate
                    || updateInfo.File == null)
                {
                    continue;
                }

                candidates.Add(new LocalModUpdateCandidate
                {
                    ResourceKey = key,
                    ResourceDisplayName = getDisplayName(resource) ?? updateInfo.ResourceDisplayName,
                    ResourceItem = resource,
                    UpdateInfo = updateInfo
                });
            }
            return candidates;
        }

        public string BuildUpdatePromptSignature(IEnumerable<LocalModUpdateCandidate> candidates)
        {
            return string.Join(
                ";",
                (candidates ?? [])
                    .OrderBy(candidate => candidate.ResourceKey, StringComparer.OrdinalIgnoreCase)
                    .Select(candidate => $"{candidate.ResourceKey}:{candidate.UpdateInfo?.CurrentVersion}->{candidate.UpdateInfo?.LatestVersion}"));
        }

        public LocalModDownloadCommand BuildUpdateDownloadCommand(
            LocalResourceItem resourceItem,
            PostFileItem file,
            string apiBaseUrl,
            string token,
            string platform,
            string apiVersion,
            PostVersionItem versionItem = null)
        {
            if (resourceItem?.Type != ExternalContentType.Mod || file == null)
            {
                return null;
            }

            string packageName = resourceItem.PackageName ?? resourceItem.ModEntity?.modInfo?.PackageName;
            return BuildDownloadCommand(file, apiBaseUrl, token, platform, apiVersion, packageName,
                resourceItem.LocalFilePath ?? resourceItem.ModEntity?.ModFilePath, versionItem, file.Type);
        }

        public LocalModDownloadCommand BuildDependencyDownloadCommand(
            PostFileItem file,
            CommunityDownloadSourceOption source,
            string apiBaseUrl,
            string token,
            string platform,
            string apiVersion)
        {
            if (file == null)
            {
                return null;
            }
            LocalModDownloadCommand command = BuildDownloadCommand(file, apiBaseUrl, token, platform, apiVersion, null, null, null, file.Type == CommunityFileType.Unknown ? CommunityFileType.Mod : file.Type);
            if (command == null)
            {
                return null;
            }
            return new LocalModDownloadCommand
            {
                Url = command.Url,
                FileName = command.FileName,
                ContentType = command.ContentType,
                IsRepositoryReleaseAsset = command.IsRepositoryReleaseAsset,
                Source = source,
                Feedback = command.Feedback
            };
        }

        private static LocalModDownloadCommand BuildDownloadCommand(
            PostFileItem file,
            string apiBaseUrl,
            string token,
            string platform,
            string apiVersion,
            string replacePackageName,
            string replacePath,
            PostVersionItem versionItem,
            CommunityFileType contentType)
        {
            if (file == null || string.IsNullOrWhiteSpace(file.Url))
            {
                return null;
            }
            return new LocalModDownloadCommand
            {
                Url = file.Url,
                FileName = CommunityFileRules.GetDownloadFileName(file),
                ContentType = CommunityFileRules.MapFileTypeToExternalContentType(contentType),
                IsRepositoryReleaseAsset = file.IsRepositoryReleaseAsset,
                Source = CommunityDownloadSourceOption.DefaultCn,
                Feedback = new CommunityDownloadFeedbackInfo
                {
                    ApiBaseUrl = apiBaseUrl,
                    Token = token,
                    FileId = file.Id,
                    PostId = file.PostId,
                    Platform = platform,
                    Version = apiVersion
                },
                ReplaceExistingModPackageName = replacePackageName,
                ReplaceExistingModPath = replacePath,
                VersionItem = versionItem
            };
        }

        public void Cancel()
        {
            // 只 Cancel 不 Dispose：被取代的在途 slot 任务仍持有其 token，
            // 运行中工作流后续 token.Register（如 HttpClient 发送）遇到已释放的 CTS 会抛
            // ObjectDisposedException 并被吞成一般失败。让 CTS 随 GC 回收即可。
            m_cancellation?.Cancel();
            m_cancellation = null;
            m_updateCheckSlot.Discard();
            m_autoUpdateScanSlot.Discard();
            m_versionsLoadSlot.Discard();
            m_dependencyLookupSlot.Discard();
        }

        public void InvalidateDependencyLookup()
        {
            m_dependencyLookupSlot.Invalidate();
        }
    }

    internal sealed class MissingModDependenciesLookupRequest : IRequestIdRequest
    {
        public int RequestId { get; set; }
        public string ResourceKey { get; set; }
        public string ResourceDisplayName { get; set; }
        public bool IsWorldUsedModsLookup { get; set; }
        public string ApiBaseUrl { get; set; }
        public string Token { get; set; }
        public IReadOnlyList<ModDependencyInfo> Dependencies { get; set; }
    }

    internal sealed class MissingModDependenciesLookupResult
    {
        public int RequestId { get; set; }
        public string ResourceKey { get; set; }
        public string ResourceDisplayName { get; set; }
        public bool IsWorldUsedModsLookup { get; set; }
        public List<PostFileItem> Files { get; set; }
        public List<string> MissingDependencyLabels { get; set; }
        public Dictionary<string, string> PreferredVersionsByPackage { get; set; }
        public string Error { get; set; }
    }
}
