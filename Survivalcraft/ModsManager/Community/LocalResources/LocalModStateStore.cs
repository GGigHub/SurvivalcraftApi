using System.Collections.Generic;

namespace Game
{
    /// <summary>
    /// 本地模组状态的进程级存储门面：实现已按职责拆到
    /// LocalModReviewState（评价缓存与帖子 ID 映射）、
    /// LocalModPendingActionStore（重启后再执行的动作）、
    /// PendingLocalModScanState（待加载模组后台扫描与兼容性覆盖）、
    /// LocalModInstallStatus（社区帖子安装/更新状态判定与社区侧缓存）。
    /// 三屏（本地/社区/我的发布）共用；社区侧只读消费（已安装/可更新标记）。
    /// </summary>
    internal static class LocalModStateStore
    {
        public static Dictionary<string, LocalModReviewEntry> LocalModReviews => LocalModReviewState.LocalModReviews;

        public static Dictionary<string, LocalModPendingActionEntry> LocalModPendingActions => LocalModPendingActionStore.LocalModPendingActions;

        public static void EnsureReviewsLoaded()
        {
            LocalModReviewState.EnsureReviewsLoaded();
        }

        public static void SaveLocalModReviews()
        {
            LocalModReviewState.SaveLocalModReviews();
        }

        public static void EnsurePendingActionsLoaded()
        {
            LocalModPendingActionStore.EnsurePendingActionsLoaded();
        }

        public static void LoadLocalModPendingActions()
        {
            LocalModPendingActionStore.LoadLocalModPendingActions();
        }

        public static void SaveLocalModPendingActions()
        {
            LocalModPendingActionStore.SaveLocalModPendingActions();
        }

        public static List<LocalResourceItem> CachedPendingLocalMods => PendingLocalModScanState.CachedPendingLocalMods;

        public static bool IsScanRunning => PendingLocalModScanState.IsScanRunning;

        public static void StartPendingLocalModsScan(bool invalidateCache = true)
        {
            PendingLocalModScanState.StartPendingLocalModsScan(invalidateCache);
        }

        public static bool UpdatePendingLocalModsScanTask()
        {
            return PendingLocalModScanState.UpdatePendingLocalModsScanTask();
        }

        public static int GetPendingRestartLocalModCount()
        {
            return PendingLocalModScanState.GetPendingRestartLocalModCount();
        }

        public static bool HasPendingLocalModCompatibilityOverride(string packageName, string filePathOrName, string version)
        {
            return PendingLocalModScanState.HasPendingLocalModCompatibilityOverride(packageName, filePathOrName, version);
        }

        public static void RememberPendingLocalModCompatibilityOverride(string packageName, string fileName, PostVersionItem version)
        {
            PendingLocalModScanState.RememberPendingLocalModCompatibilityOverride(packageName, fileName, version);
        }

        public static bool IsPendingLocalModIncompatibleWithCurrentGame(PendingLocalModInfo info)
        {
            return PendingLocalModScanState.IsPendingLocalModIncompatibleWithCurrentGame(info);
        }

        public static bool IsVersionTextIncompatible(string currentVersion, string candidateVersion)
        {
            return PendingLocalModScanState.IsVersionTextIncompatible(currentVersion, candidateVersion);
        }

        public static string FindExactLocalDownloadFilePath(ExternalContentType type, string fileName)
        {
            return LocalModInstallStatus.FindExactLocalDownloadFilePath(type, fileName);
        }

        public static List<LocalResourceItem> GetCommunityLocalModSnapshot()
        {
            return LocalModInstallStatus.GetCommunityLocalModSnapshot();
        }

        public static int GetCachedLocalModPostId(LocalResourceItem resourceItem)
        {
            return LocalModReviewState.GetCachedLocalModPostId(resourceItem);
        }

        public static void RememberLocalModPostId(string importedName, string fileName, int postId)
        {
            LocalModReviewState.RememberLocalModPostId(importedName, fileName, postId);
        }

        public static void RememberLocalPostId(string resourceKey, string displayName, string fileName, int postId)
        {
            LocalModReviewState.RememberLocalPostId(resourceKey, displayName, fileName, postId);
        }

        public static bool IsInstalledCommunityModVersion(PostVersionItem version)
        {
            return LocalModInstallStatus.IsInstalledCommunityModVersion(version);
        }

        public static bool IsInstalledCommunityModVersion(PostVersionItem version, List<LocalResourceItem> communityLocalMods)
        {
            return LocalModInstallStatus.IsInstalledCommunityModVersion(version, communityLocalMods);
        }

        public static Dictionary<string, LocalModCommunityStatusInfo> LocalModCommunityStatusCache => LocalModInstallStatus.LocalModCommunityStatusCache;

        public static Dictionary<string, LocalModUpdateCheckResult> LocalModUpdateDetailsCache => LocalModInstallStatus.LocalModUpdateDetailsCache;
    }
}
