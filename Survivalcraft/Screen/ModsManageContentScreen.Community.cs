using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Engine;
using Game;

public partial class ModsManageContentScreen
{
    private static bool s_autoUpdateScanLaunched;
    private readonly LocalModNetworkManager m_localModNetworkManager = new();
    private readonly ReviewSubmissionHandler m_reviewHandler =
        new((message, autoHideSeconds) => CommunityToastManager.Show(message, autoHideSeconds: autoHideSeconds));
    private readonly TaskSlot<LocalPublishedCheckResult> m_publishedCheckSlot = new(LanguageControl.Get("ScbbsPlusTaskSlot", "PublishedCheck"));

    // 当前选中实体（对话框动作入口显式传参，不依赖选中态；此字段供结果落地的 reviewKey 比对）
    private ModEntity m_selectedEntity;

    // ModEntity 重写 Equals（包名+版本），必须引用比较器才能一一映射
    private sealed class EntityReferenceComparer : IEqualityComparer<ModEntity>
    {
        public bool Equals(ModEntity x, ModEntity y) => ReferenceEquals(x, y);
        public int GetHashCode(ModEntity obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }

    private readonly List<LocalResourceItem> m_localModItems = [];
    private readonly Dictionary<ModEntity, LocalResourceItem> m_localModItemByEntity = new(new EntityReferenceComparer());
    // 已发布检查在途态（版本列表/评分共用）：key → 等待回调
    private string m_publishedCheckResourceKey;
    private Action<int> m_publishedCheckCallback;
    private int m_publishedCheckPostId;
    private bool m_publishedCheckCompleted;
    // 评分提交在途的 resourceKey（落地时比对）
    private string m_localModScoreResourceKey;

    private static LocalResourceItem MakeLocalResourceItem(ModEntity entity)
    {
        // spec §5.1：轻量映射，不动已移植 BuildLocalMods（pending 语义不同）
        return new LocalResourceItem
        {
            Type = ExternalContentType.Mod,
            ModEntity = entity,
            LocalFilePath = entity.ModFilePath,
            Name = string.IsNullOrEmpty(entity.ModFilePath) ? null : Storage.GetFileName(entity.ModFilePath),
            DisplayName = entity.modInfo?.Name,
            PackageName = entity.modInfo?.PackageName,
            Version = entity.modInfo?.Version,
            IsDisabled = entity.IsDisabled,
            DisableReason = entity.DisableReason,
            RequiresRestartToLoad = entity.IsPendingRestart
        };
    }

    private void RebuildLocalModItems()
    {
        m_localModItems.Clear();
        m_localModItemByEntity.Clear();
        foreach (ModEntity entity in ModsManager.ModListAll)
        {
            if (entity == null || ReferenceEquals(entity, ModsManager.FastDebugModEntity)) continue;
            if (entity.modInfo == null) continue;
            if (entity.modInfo.PackageName is "survivalcraft" or "fastdebug") continue;
            LocalResourceItem item = MakeLocalResourceItem(entity);
            m_localModItems.Add(item);
            m_localModItemByEntity[entity] = item;
        }
    }

    private LocalResourceItem GetLocalModItem(ModEntity entity)
        => m_localModItemByEntity.TryGetValue(entity, out LocalResourceItem item) ? item : MakeLocalResourceItem(entity);

    private void LaunchAutoUpdateScanIfNeeded()
    {
        if (s_autoUpdateScanLaunched || m_localModItems.Count == 0) { s_autoUpdateScanLaunched = true; return; }
        s_autoUpdateScanLaunched = true;
        // spec §3.1：门闩发起时置位，失败不重扫
        string signature = LocalResourceIdentity.BuildModAutoUpdateScanSignature(m_localModItems);
        m_localModNetworkManager.StartAutoUpdateScan(new LocalModAutoUpdateScanRequest
        {
            Signature = signature,
            ApiBaseUrl = AuthSessionManager.Instance.ApiBaseUrl,
            Token = AuthSessionManager.Instance.CurrentToken,
            CurrentApiVersion = VersionCatalogCache.CurrentApiVersion,
            CurrentGameVersion = VersionCatalogCache.CurrentGameVersion,
            VersionCatalog = VersionCatalogCache.CachedVersionCatalog,
            GameVersionsById = VersionCatalogCache.CachedGameVersionsById,
            Targets = m_localModItems.Select(item => new LocalModUpdateTarget
            {
                ResourceKey = LocalResourceIdentity.MakeModReviewKey(item),
                DisplayName = item.DisplayName ?? item.Name,
                FileName = CommunityUploadFileProvider.GetUploadFileName(item, item.LocalFilePath),
                PackageName = item.PackageName,
                CurrentVersion = item.Version,
                RepositoryUrl = TryNormalizeHttpUrl(item.ModEntity?.modInfo?.Link),
                CachedPostId = LocalModStateStore.GetCachedLocalModPostId(item)
            }).ToArray()
        });
    }

    private static string TryNormalizeHttpUrl(string value)
    {
        // 与模组 LocalResources.cs:872 同语义：http(s) 才返回，否则 null
        if (string.IsNullOrWhiteSpace(value)) return null;
        string trimmed = value.Trim();
        return trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? trimmed : null;
    }

    private static string GetLocalModDisplayName(LocalResourceItem resourceItem)
        => resourceItem?.DisplayName ?? resourceItem?.Name ?? LanguageControl.Get("ScbbsPlusLocalScreen", "Text", "LocalMod");

    private void UpdateAutoUpdateScanTask()
    {
        if (!m_localModNetworkManager.TryTakeAutoUpdateScan(out LocalModAutoUpdateScanResult result)) return;
        if (result == null || result.RequestId != m_localModNetworkManager.CurrentAutoUpdateScanRequestId) return;
        if (!string.IsNullOrEmpty(result.Error))
        {
            Log.Warning(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusLocalMods", "AutoUpdateScanFailed"), result.Error));
            return;
        }
        HashSet<string> activeKeys = new(StringComparer.OrdinalIgnoreCase);
        if (result.Entries != null)
        {
            foreach (LocalModAutoUpdateScanEntry entry in result.Entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.ResourceKey) || entry.Status == null) continue;
                activeKeys.Add(entry.ResourceKey);
                LocalModStateStore.LocalModCommunityStatusCache[entry.ResourceKey] = entry.Status;
                if (entry.Status.IsPublished && entry.Status.PostId > 0)
                    LocalModStateStore.RememberLocalPostId(entry.ResourceKey, entry.DisplayName, entry.FileName, entry.Status.PostId);
                if (entry.UpdateInfo?.HasUpdate == true && entry.UpdateInfo.File != null)
                    LocalModStateStore.LocalModUpdateDetailsCache[entry.ResourceKey] = entry.UpdateInfo;
                else
                    LocalModStateStore.LocalModUpdateDetailsCache.Remove(entry.ResourceKey);
            }
        }
        foreach (string staleKey in LocalModStateStore.LocalModUpdateDetailsCache.Keys.Where(k => !activeKeys.Contains(k)).ToArray())
            LocalModStateStore.LocalModUpdateDetailsCache.Remove(staleKey);
        RefreshModList();
        ShowUpdateToast();
    }

    private void ShowUpdateToast()
    {
        // spec §5.3：UpdatesDialog 不移植，Toast 纯提示；计数走 BuildUpdateCandidates
        int count = m_localModNetworkManager.BuildUpdateCandidates(
            m_localModItems,
            LocalModStateStore.LocalModUpdateDetailsCache,
            LocalResourceIdentity.MakeModReviewKey,
            item => item.DisplayName ?? item.Name).Count;
        if (count > 0)
        {
            CommunityToastManager.Show(
                string.Format(CultureInfo.InvariantCulture,
                    LanguageControl.Get("ScbbsPlusCommunityScreen", "Local", "LocalMods", "Network", "ModsCanBeUpdatedTapToView"), count),
                autoHideSeconds: 3.0);
        }
    }

    // ============ 动作入口（ModDetailsDialog 经屏引用调用） ============

    /// <summary>检查更新（模组 StartLocalModUpdateCheck 移植）。</summary>
    internal void CheckModUpdate(ModEntity entity)
    {
        LocalResourceItem resourceItem = GetLocalModItem(entity);
        if (!TryGuardLocalModNetworkStart(m_localModNetworkManager.IsUpdateCheckRunning, "CheckingPreviousModUpdate", () => resourceItem?.Type == ExternalContentType.Mod))
        {
            return;
        }
        string key = LocalResourceIdentity.MakeModReviewKey(resourceItem);
        int cachedPostId = LocalModStateStore.GetCachedLocalModPostId(resourceItem);
        string token = AuthSessionManager.Instance.CurrentToken;
        string apiBaseUrl = AuthSessionManager.Instance.ApiBaseUrl;
        string displayName = GetLocalModDisplayName(resourceItem);
        string fileName = CommunityUploadFileProvider.GetUploadFileName(resourceItem, resourceItem.LocalFilePath);
        string currentVersion = resourceItem.ModEntity?.modInfo?.Version ?? resourceItem.Version;
        string packageName = resourceItem.PackageName ?? resourceItem.ModEntity?.modInfo?.PackageName;
        m_localModNetworkManager.StartUpdateCheck(new LocalModUpdateCheckRequest
        {
            ResourceKey = key,
            ResourceDisplayName = displayName,
            CachedPostId = cachedPostId,
            ApiBaseUrl = apiBaseUrl,
            Token = token,
            FileName = fileName,
            CurrentVersion = currentVersion,
            PackageName = packageName,
            RepositoryUrl = TryNormalizeHttpUrl(resourceItem.ModEntity?.modInfo?.Link),
            CurrentApiVersion = VersionCatalogCache.CurrentApiVersion,
            CurrentGameVersion = VersionCatalogCache.CurrentGameVersion,
            RequirePublishedPost = true
        });
    }

    /// <summary>版本列表（内含已发布检查）。</summary>
    internal void OpenModVersions(ModEntity entity)
    {
        EnsurePublishedPostThen(entity, _ => StartLocalModVersionsLoad(GetLocalModItem(entity)));
    }

    /// <summary>缺失依赖查找。</summary>
    /// <summary>本地快照判缺失依赖：详情对话框据此隐藏「缺失依赖」按钮（无缺失不显）。</summary>
    internal static bool HasMissingDependencies(ModEntity entity)
    {
        if (entity?.modInfo == null)
        {
            return false;
        }
        return LocalResourceCollection.CaptureDependencySnapshot(null, () => ModsManager.ModListAll, null)
            .GetUnavailableDependencies(ModDependencyParser.GetDependencies(entity)).ToArray().Length > 0;
    }

    internal void LookupModDependencies(ModEntity entity)
    {
        LocalResourceItem resourceItem = GetLocalModItem(entity);
        if (!TryGuardLocalModNetworkStart(m_localModNetworkManager.IsDependencyLookupRunning, "SearchingPreviousDependency", () => resourceItem?.Type == ExternalContentType.Mod))
        {
            return;
        }
        IReadOnlyList<ModDependencyInfo> dependencies = LocalResourceCollection.CaptureDependencySnapshot(null, () => ModsManager.ModListAll, null)
            .GetUnavailableDependencies(ModDependencyParser.GetDependencies(entity))
            .ToArray();
        if (dependencies.Count == 0)
        {
            return;
        }
        m_localModNetworkManager.StartDependencyLookup(new MissingModDependenciesLookupRequest
        {
            ResourceKey = LocalResourceIdentity.MakeModReviewKey(resourceItem),
            ResourceDisplayName = GetLocalModDisplayName(resourceItem),
            ApiBaseUrl = AuthSessionManager.Instance.ApiBaseUrl,
            Token = AuthSessionManager.Instance.CurrentToken,
            Dependencies = dependencies
        });
    }


    /// <summary>「上传」按钮网址：域名取当前中文服务器 WebsiteUrl（自带 #/modList/0 路径，
    /// 截 scheme+authority 后重拼发布页）；WebsiteUrl 缺失/不可解析时退 ApiUrl。</summary>
    internal static string GetChinesePublishPostUrl()
    {
        CommunityServerManager.Info info = CommunityServerManager.CurrentChineseInfo;
        string authority = TryGetHttpAuthority(info?.WebsiteUrl) ?? TryGetHttpAuthority(info?.ApiUrl);
        return (authority ?? string.Empty).TrimEnd('/') + "/#/publish/post";
    }

    private static string TryGetHttpAuthority(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }
        return Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri.GetLeftPart(UriPartial.Authority)
            : null;
    }

    /// <summary>评分（内含已发布检查）。</summary>
    internal void OpenModReview(ModEntity entity)
    {
        EnsurePublishedPostThen(entity, _ => OpenLocalModReviewDialog(GetLocalModItem(entity)));
    }

    // ============ 更新检查/版本列表/依赖落地（模组 LocalMods.Network.cs 移植） ============

    /// <summary>网络任务入口共用守卫：同任务在跑则弹「请稍候」，资源校验不过静默放弃。</summary>
    private bool TryGuardLocalModNetworkStart(bool isRunning, string busyMessageKey, Func<bool> validateResource)
    {
        if (isRunning)
        {
            DialogsManager.ShowDialog(null, new MessageDialog(LanguageControl.Get("ScbbsPlusLocalScreen", "Text", "PleaseWait"), LanguageControl.Get("ScbbsPlusCommunityScreen", "Local", "LocalMods", "Network", busyMessageKey), LanguageControl.Ok, null, null));
            return false;
        }
        return validateResource();
    }

    private void UpdateUpdateCheckTask()
    {
        if (!m_localModNetworkManager.TryTakeUpdateCheck(out LocalModUpdateCheckResult result))
        {
            return;
        }
        if (result == null || result.RequestId != m_localModNetworkManager.CurrentUpdateCheckRequestId)
        {
            return;
        }
        LocalResourceItem currentItem = m_selectedEntity != null ? GetLocalModItem(m_selectedEntity) : null;
        if (!string.IsNullOrEmpty(result.ResourceKey)
            && (currentItem?.Type != ExternalContentType.Mod
                || !string.Equals(result.ResourceKey, LocalResourceIdentity.MakeModReviewKey(currentItem), StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }
        if (!string.IsNullOrEmpty(result.Error))
        {
            DialogsManager.ShowDialog(null, new MessageDialog(LanguageControl.Error, string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusCommunityScreen", "Local", "LocalMods", "Network", "UpdateCheckFailed"), result.Error), LanguageControl.Ok, null, null));
            return;
        }

        LocalModStateStore.RememberLocalPostId(result.ResourceKey, result.ResourceDisplayName, result.SourceFileName, result.PostId);
        string currentVersion = string.IsNullOrWhiteSpace(result.CurrentVersion) ? "-" : result.CurrentVersion;
        string latestVersion = string.IsNullOrWhiteSpace(result.LatestVersion) ? "-" : result.LatestVersion;
        if (!result.HasUpdate)
        {
            string message = string.Format(
                CultureInfo.InvariantCulture,
                LanguageControl.Get("ScbbsPlusCommunityScreen", "Local", "LocalMods", "Network", "AlreadyUpToDateFormat"),
                LanguageControl.Get("ScbbsPlusLocalScreen", "Text", "CurrentVersion"),
                currentVersion,
                LanguageControl.Get("ScbbsPlusLocalScreen", "Text", "LatestVersion"),
                latestVersion);
            DialogsManager.ShowDialog(
                null,
                new MessageDialog(
                    LanguageControl.Get("ScbbsPlusLocalScreen", "Text", "CheckUpdate"),
                    string.IsNullOrWhiteSpace(result.NoUpdateReason)
                        ? message
                        : string.Format(
                            CultureInfo.InvariantCulture,
                            LanguageControl.Get("ScbbsPlusCommunityScreen", "Local", "LocalMods", "Network", "NoUpdateReasonFormat"),
                            result.NoUpdateReason,
                            LanguageControl.Get("ScbbsPlusLocalScreen", "Text", "CurrentVersion"),
                            currentVersion,
                            LanguageControl.Get("ScbbsPlusLocalScreen", "Text", "LatestVersion"),
                            latestVersion),
                    LanguageControl.Ok,
                    null,
                    null));
            return;
        }

        ShowLocalModUpdateFoundDialog(result, currentItem);
    }

    /// <summary>“发现更新”详情对话框唯一构造点：标题/版本行/下载文件名均从检查结果推导。</summary>
    private void ShowLocalModUpdateFoundDialog(LocalModUpdateCheckResult result, LocalResourceItem resourceItem)
    {
        string releaseNotes = result?.ReleaseNotes;
        string contentHtml = string.IsNullOrWhiteSpace(releaseNotes)
            ? CommunityVersionDetailFormatter.GetVersionDetailHtml(result?.LatestVersionItem)
            : null;
        LocalResourceItem downloadItem = resourceItem;
        DialogsManager.ShowDialog(
            null,
            new CommunityPostDetailsDialog(
                LanguageControl.Get("ScbbsPlusLocalScreen", "Text", "UpdateFound"),
                contentHtml,
                AuthSessionManager.Instance.ApiBaseUrl,
                LanguageControl.Get("ScbbsPlusCommon", "Text", "Download"),
                () => StartLocalModUpdateDownload(downloadItem, result.File, result.LatestVersionItem),
                enableAutoTranslate: true,
                contentPlainText: releaseNotes,
                buildHeader: panel => PostDetailsHeaderBuilder.AppendUpdateInfo(
                    panel,
                    result?.PostTitle ?? LanguageControl.Get("ScbbsPlusLocalScreen", "Text", "CommunityPost"),
                    result?.CurrentVersion,
                    result?.LatestVersion,
                    CommunityFileRules.GetDownloadFileName(result?.File))));
    }

    private void StartLocalModUpdateDownload(LocalResourceItem resourceItem, PostFileItem file, PostVersionItem version = null)
    {
        LocalModDownloadCommand command = m_localModNetworkManager.BuildUpdateDownloadCommand(
            resourceItem,
            file,
            AuthSessionManager.Instance.ApiBaseUrl,
            AuthSessionManager.Instance.CurrentToken,
            DownloadFlowManager.GetPlatformName(),
            VersionCatalogCache.CurrentApiVersion,
            version);
        if (command == null || !command.IsValid)
        {
            return;
        }
        LocalModStateStore.RememberPendingLocalModCompatibilityOverride(
            command.ReplaceExistingModPackageName,
            command.FileName,
            command.VersionItem);
        DownloadFlowManager.Instance.StartFileDownload(
            command.Url,
            command.FileName,
            command.ContentType,
            requiresRestart: true,
            command.Feedback,
            replaceExistingModPackageName: command.ReplaceExistingModPackageName,
            replaceExistingModPath: command.ReplaceExistingModPath);
    }

    private void StartLocalModVersionsLoad(LocalResourceItem resourceItem)
    {
        if (!TryGuardLocalModNetworkStart(m_localModNetworkManager.IsVersionsLoadRunning, "LoadingPreviousVersionList", () => resourceItem?.Type == ExternalContentType.Mod))
        {
            return;
        }

        string key = LocalResourceIdentity.MakeModReviewKey(resourceItem);
        if (!HasCurrentLocalPublishedPost(key))
        {
            return;
        }

        int cachedPostId = m_publishedCheckPostId > 0 ? m_publishedCheckPostId : LocalModStateStore.GetCachedLocalModPostId(resourceItem);
        m_localModNetworkManager.StartVersionsLoad(new LocalModVersionsRequest
        {
            ResourceKey = key,
            ResourceDisplayName = GetLocalModDisplayName(resourceItem),
            CachedPostId = cachedPostId,
            ApiBaseUrl = AuthSessionManager.Instance.ApiBaseUrl,
            Token = AuthSessionManager.Instance.CurrentToken,
            FileName = CommunityUploadFileProvider.GetUploadFileName(resourceItem, resourceItem.LocalFilePath),
            PackageName = resourceItem.PackageName ?? resourceItem.ModEntity?.modInfo?.PackageName,
            CurrentVersion = resourceItem.ModEntity?.modInfo?.Version ?? resourceItem.Version,
            CurrentApiVersion = VersionCatalogCache.CurrentApiVersion,
            CurrentGameVersion = VersionCatalogCache.CurrentGameVersion
        });
    }

    private void UpdateVersionsLoadTask()
    {
        if (!m_localModNetworkManager.TryTakeVersionsLoad(out LocalModVersionsLoadResult result))
        {
            return;
        }
        LocalResourceItem currentItem = m_selectedEntity != null ? GetLocalModItem(m_selectedEntity) : null;
        if (result == null || result.RequestId != m_localModNetworkManager.CurrentVersionsLoadRequestId)
        {
            return;
        }
        if (!string.IsNullOrEmpty(result.ResourceKey)
            && (currentItem?.Type != ExternalContentType.Mod
                || !string.Equals(result.ResourceKey, LocalResourceIdentity.MakeModReviewKey(currentItem), StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }
        if (!string.IsNullOrEmpty(result.Error))
        {
            DialogsManager.ShowDialog(null, new MessageDialog(LanguageControl.Error, string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusCommunityScreen", "Local", "LocalMods", "Network", "FailedToLoadVersionList"), result.Error), LanguageControl.Ok, null, null));
            return;
        }

        LocalModStateStore.RememberLocalPostId(result.ResourceKey, result.ResourceDisplayName, result.SourceFileName, result.PostId);
        DialogsManager.ShowDialog(
            null,
            new CommunityModVersionsDialog(
                result.PostTitle,
                result.CurrentVersion,
                result.CurrentApiVersion,
                result.CurrentGameVersion,
                result.Versions,
                result.GameVersionsById,
                result.VersionCatalog,
                ShowLocalModVersionDetails,
                StartLocalModVersionDownload));
    }

    private void ShowLocalModVersionDetails(PostVersionItem version)
    {
        LocalResourceItem resourceItem = m_selectedEntity != null ? GetLocalModItem(m_selectedEntity) : null;
        string title = string.IsNullOrWhiteSpace(version?.Title)
            ? LanguageControl.Get("ScbbsPlusLocalScreen", "Text", "VersionDetails")
            : version.Title;
        string packageName = resourceItem?.PackageName ?? resourceItem?.ModEntity?.modInfo?.PackageName;
        string fileName = CommunityUploadFileProvider.GetUploadFileName(resourceItem, resourceItem?.LocalFilePath);
        PostFileItem file = ModUpdateSelector.FindBestFile(version, packageName, fileName);
        DialogsManager.ShowDialog(
            null,
            new CommunityPostDetailsDialog(
                title,
                CommunityVersionDetailFormatter.GetVersionDetailHtml(version),
                AuthSessionManager.Instance.ApiBaseUrl,
                file != null ? LanguageControl.Get("ScbbsPlusCommon", "Text", "Download") : null,
                file != null ? () => StartLocalModUpdateDownload(resourceItem, file, version) : null,
                enableAutoTranslate: true));
    }

    private void StartLocalModVersionDownload(PostVersionItem version, PostFileItem file)
    {
        StartLocalModUpdateDownload(m_selectedEntity != null ? GetLocalModItem(m_selectedEntity) : null, file, version);
    }

    private void UpdateDependencyLookupTask()
    {
        if (!m_localModNetworkManager.TryTakeDependencyLookup(out MissingModDependenciesLookupResult result))
        {
            return;
        }
        LocalResourceItem currentItem = m_selectedEntity != null ? GetLocalModItem(m_selectedEntity) : null;
        if (result == null || result.RequestId != m_localModNetworkManager.CurrentDependencyLookupRequestId)
        {
            return;
        }
        if (!string.IsNullOrEmpty(result.ResourceKey)
            && !m_localModNetworkManager.IsCurrentDependencyResult(
                result,
                currentItem?.Type == ExternalContentType.Mod ? LocalResourceIdentity.MakeModReviewKey(currentItem) : null,
                null))
        {
            return;
        }
        if (!string.IsNullOrEmpty(result.Error))
        {
            DialogsManager.ShowDialog(
                null,
                new MessageDialog(
                    LanguageControl.Error,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        LanguageControl.Get("ScbbsPlusCommunityScreen", "Local", "LocalMods", "Network", "DependencySearchFailed"),
                        result.Error),
                    LanguageControl.Ok,
                    null,
                    null));
            return;
        }
        bool hasFiles = result.Files != null && result.Files.Count > 0;
        if (!hasFiles)
        {
            DialogsManager.ShowDialog(
                null,
                new MessageDialog(
                    LanguageControl.Get("ScbbsPlusCommunityScreen", "Local", "LocalMods", "Network", "SearchDependencies"),
                    LanguageControl.Get("ScbbsPlusCommunityScreen", "Local", "LocalMods", "Network", "NoDownloadableDependenciesWereFound"),
                    LanguageControl.Ok,
                    null,
                    null));
            return;
        }

        DialogsManager.ShowDialog(
            null,
            new CommunityMissingDependenciesDialog(
                result.ResourceDisplayName,
                result.Files,
                StartMissingDependencyDownload,
                StartMissingDependencyDownloads,
                groupFilesByPackageName: true,
                preferredVersionsByPackage: result.PreferredVersionsByPackage));
    }

    private void StartMissingDependencyDownload(PostFileItem file)
    {
        if (m_localModNetworkManager.FilterDownloadableFiles([file]).Count == 0)
        {
            return;
        }
        EnqueueMissingDependencyDownload(file, DownloadSourceOption.DefaultCn);
    }

    private void StartMissingDependencyDownloads(IReadOnlyList<PostFileItem> files)
    {
        IReadOnlyList<PostFileItem> validFiles = m_localModNetworkManager.FilterDownloadableFiles(files);
        if (validFiles.Count == 0)
        {
            return;
        }
        foreach (PostFileItem file in validFiles)
        {
            EnqueueMissingDependencyDownload(file, DownloadSourceOption.DefaultCn);
        }
    }

    private void EnqueueMissingDependencyDownload(PostFileItem file, DownloadSourceOption source)
    {
        if (file == null || string.IsNullOrWhiteSpace(file.Url))
        {
            return;
        }
        LocalModDownloadCommand command = m_localModNetworkManager.BuildDependencyDownloadCommand(
            file,
            source,
            AuthSessionManager.Instance.ApiBaseUrl,
            AuthSessionManager.Instance.CurrentToken,
            DownloadFlowManager.GetPlatformName(),
            VersionCatalogCache.CurrentApiVersion);
        if (command == null || !command.IsValid)
        {
            return;
        }
        DownloadFlowManager.Instance.EnqueueFileDownload(
            command.Url,
            command.FileName,
            command.ContentType,
            requiresRestart: true,
            command.Source,
            command.Feedback);
    }

    // ============ 已发布检查/评分（模组 LocalMods.Review.cs 移植，world 系与时效校验裁掉） ============

    /// <summary>
    /// 已发布检查放行门（spec §3.5）：reviewKey 命中 LocalModReviews PostId>0 直接回调；
    /// 否则跑 published-check TaskSlot，落地 PostId>0 回调，PostId==0/错误 Toast。
    /// </summary>
    private void EnsurePublishedPostThen(ModEntity entity, Action<int> onPublished)
    {
        LocalResourceItem resourceItem = GetLocalModItem(entity);
        string key = LocalResourceIdentity.MakeModReviewKey(resourceItem);
        if (!string.IsNullOrWhiteSpace(key)
            && LocalModStateStore.LocalModReviews.TryGetValue(key, out LocalModReviewEntry entry)
            && entry?.PostId > 0)
        {
            onPublished(entry.PostId);
            return;
        }
        string displayName = GetLocalModDisplayName(resourceItem);
        string fileName = CommunityUploadFileProvider.GetUploadFileName(resourceItem, resourceItem.LocalFilePath);
        string packageName = resourceItem.PackageName ?? entity?.modInfo?.PackageName;
        StartLocalPublishedCheck(key, displayName, fileName, packageName, onPublished);
    }

    private void StartLocalPublishedCheck(string key, string displayName, string fileName, string packageName, Action<int> onPublished)
    {
        m_publishedCheckResourceKey = key ?? string.Empty;
        m_publishedCheckPostId = 0;
        m_publishedCheckCompleted = false;
        m_publishedCheckCallback = onPublished;

        if (string.IsNullOrWhiteSpace(key) || (string.IsNullOrWhiteSpace(fileName) && string.IsNullOrWhiteSpace(packageName)))
        {
            // 本体简化：键非法直接 Toast，不走在途落地（模组侧落按钮禁用态，本屏无该状态）。
            m_publishedCheckCompleted = true;
            m_publishedCheckCallback = null;
            CommunityToastManager.Show(
                LanguageControl.Get("ScbbsPlusCommunityScreen", "Local", "LocalMods", "Review", "MissingFileNameOrPackageName"),
                autoHideSeconds: 3.0);
            return;
        }

        m_publishedCheckSlot.Start((requestId, _) => CheckPublishedAsync(
            LocalModPostResolver.Instance,
            new LocalPublishedCheckRequest
            {
                RequestId = requestId,
                ResourceKey = key,
                DisplayName = displayName,
                FileName = fileName,
                PackageName = packageName,
                ApiBaseUrl = AuthSessionManager.Instance.ApiBaseUrl,
                Token = AuthSessionManager.Instance.CurrentToken
            }));
    }

    /// <summary>
    /// 检查本地资源是否已发布到社区（按文件名/包名解析帖子）。resolver 经参数穿线
    /// （生产传组合根静态字段，测试传 HTTP 桩）。
    /// </summary>
    private static async Task<LocalPublishedCheckResult> CheckPublishedAsync(
        LocalModPostResolver postResolver,
        LocalPublishedCheckRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        try
        {
            int postId = await postResolver.ResolvePostIdAsync(
                request.ApiBaseUrl,
                request.Token,
                request.FileName,
                request.PackageName,
                cancellationToken);
            return new LocalPublishedCheckResult
            {
                RequestId = request.RequestId,
                ResourceKey = request.ResourceKey,
                DisplayName = request.DisplayName,
                FileName = request.FileName,
                PostId = postId
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 用户取消：传播，不上报为查询错误。
            throw;
        }
        catch (Exception ex)
        {
            return new LocalPublishedCheckResult
            {
                RequestId = request.RequestId,
                ResourceKey = request.ResourceKey,
                DisplayName = request.DisplayName,
                FileName = request.FileName,
                Error = ex.Message
            };
        }
    }

    private void UpdatePublishedCheckTask()
    {
        if (!m_publishedCheckSlot.TryTake(out LocalPublishedCheckResult result, ex =>
            {
                // 任务 Faulted 不冒泡出 Update()，按无结果丢弃。
                Log.Warning(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusLocalMods", "PublishedCheckTaskFaulted"), ex.Message));
                return null;
            }))
        {
            return;
        }

        if (result == null
            || !string.Equals(result.ResourceKey, m_publishedCheckResourceKey, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        m_publishedCheckPostId = result.PostId;
        m_publishedCheckCompleted = true;
        Action<int> callback = m_publishedCheckCallback;
        m_publishedCheckCallback = null;
        if (result.PostId > 0)
        {
            LocalModStateStore.RememberLocalPostId(result.ResourceKey, result.DisplayName, result.FileName, result.PostId);
            callback?.Invoke(result.PostId);
        }
        else
        {
            // spec §6：未发布/失败均 Toast（模组侧落按钮禁用态，本屏无该状态）。
            CommunityToastManager.Show(
                string.IsNullOrEmpty(result.Error)
                    ? LanguageControl.Get("ScbbsPlusCommunityScreen", "Local", "LocalMods", "Review", "Buttons", "NoPost")
                    : result.Error,
                autoHideSeconds: 3.0);
        }
    }

    private bool HasCurrentLocalPublishedPost(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        if (string.Equals(key, m_publishedCheckResourceKey, StringComparison.OrdinalIgnoreCase)
            && m_publishedCheckCompleted
            && m_publishedCheckPostId > 0)
        {
            return true;
        }

        return LocalModStateStore.LocalModReviews.TryGetValue(key, out LocalModReviewEntry entry)
            && entry?.PostId > 0;
    }

    private static bool IsReviewableLocalResource(LocalResourceItem resourceItem)
    {
        if (resourceItem == null || resourceItem.IsBuiltIn)
        {
            return false;
        }

        return resourceItem.Type == ExternalContentType.FurniturePack
            || resourceItem.Type == ExternalContentType.BlocksTexture
            || resourceItem.Type == ExternalContentType.CharacterSkin
            || resourceItem.Type == ExternalContentType.Mod;
    }

    private void OpenLocalModReviewDialog(LocalResourceItem resourceItem)
    {
        if (!IsReviewableLocalResource(resourceItem))
        {
            return;
        }

        string key = LocalResourceIdentity.MakeModReviewKey(resourceItem);
        LocalModStateStore.LocalModReviews.TryGetValue(key, out LocalModReviewEntry entry);
        string title = resourceItem.DisplayName ?? resourceItem.Name ?? LanguageControl.Get("ScbbsPlusLocalScreen", "Text", "LocalResource");
        DialogsManager.ShowDialog(
            null,
            new CommunityModReviewDialog(
                title,
                entry?.Rating ?? 0,
                entry?.Text ?? string.Empty,
                entry?.LastText ?? entry?.Text ?? string.Empty,
                (rating, text) =>
                {
                    StartLocalModScore(resourceItem, rating, text);
                }
            )
        );
    }

    private void StartLocalModScore(LocalResourceItem resourceItem, int rating, string text)
    {
        if (!IsReviewableLocalResource(resourceItem))
        {
            return;
        }
        string key = LocalResourceIdentity.MakeModReviewKey(resourceItem);
        int cachedPostId = LocalModStateStore.GetCachedLocalModPostId(resourceItem);
        string fileName = CommunityUploadFileProvider.GetUploadFileName(resourceItem, resourceItem.LocalFilePath);
        double playSeconds = resourceItem.ModEntity == null ? 0.0 : PlayedTimeTracker.GetTotalSeconds(resourceItem.ModEntity);
        string packageName = resourceItem.Type == ExternalContentType.Mod
            ? resourceItem.ModEntity?.modInfo?.PackageName
            : null;
        StartLocalScore(key, cachedPostId, fileName, playSeconds, rating, text, packageName);
    }

    // 评分提交入口统一走这里，后续如果内置模组想改成别的社区接口，只需要替换这一层而不用动各个按钮回调。
    private void StartLocalScore(
        string key,
        int cachedPostId,
        string fileName,
        double playSeconds,
        int rating,
        string text,
        string packageName = null,
        string successToastMessage = null,
        bool showSubmittingDialog = true)
    {
        bool started = m_reviewHandler.TrySubmit(new ReviewSubmissionRequest
        {
            ResourceKey = key,
            CachedPostId = cachedPostId,
            FileName = fileName,
            PackageName = packageName,
            PlaySeconds = playSeconds,
            Rating = rating,
            Content = text,
            SuccessToastMessage = successToastMessage,
            ShowSubmittingDialog = showSubmittingDialog
        });
        if (!started)
        {
            return;
        }
        m_localModScoreResourceKey = key;
    }

    private void UpdateModScoreTask()
    {
        m_reviewHandler.TryTakeResult(result =>
            string.Equals(result.ResourceKey, m_localModScoreResourceKey, StringComparison.OrdinalIgnoreCase));
    }
}
