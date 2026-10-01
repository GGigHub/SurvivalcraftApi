using Engine;

namespace Game {
    /// <summary>
    /// 本地模组状态的进程级存储门面：实现已按职责拆到
    /// LocalModReviewState（评价缓存与帖子 ID 映射）、
    /// LocalModPendingActionStore（重启后再执行的动作）、
    /// LocalModInstallStatus（社区帖子安装/更新状态判定与社区侧缓存）。
    /// 待重启模组信息直接投影 ModsManager.ModListAll（IsPendingRestart 实体），
    /// 下载前兼容覆盖缓存由本类自持。
    /// 三屏（本地/社区/我的发布）共用；社区侧只读消费（已安装/可更新标记）。
    /// </summary>
    static class LocalModStateStore {
        // 下载前兼容判定缓存自持实例：扫描基础设施删除后，判定点收敛在投影处（LocalResourceCollection.ToLocalResourceItem）
        static readonly PendingLocalModCompatibilityOverrideCache s_compatibilityOverrides = new();

        public static Dictionary<string, LocalModReviewEntry> LocalModReviews => LocalModReviewState.LocalModReviews;

        public static Dictionary<string, LocalModPendingActionEntry> LocalModPendingActions => LocalModPendingActionStore.LocalModPendingActions;

        public static void EnsureReviewsLoaded() {
            LocalModReviewState.EnsureReviewsLoaded();
        }

        public static void SaveLocalModReviews() {
            LocalModReviewState.SaveLocalModReviews();
        }

        public static void EnsurePendingActionsLoaded() {
            LocalModPendingActionStore.EnsurePendingActionsLoaded();
        }

        public static void LoadLocalModPendingActions() {
            LocalModPendingActionStore.LoadLocalModPendingActions();
        }

        public static void SaveLocalModPendingActions() {
            LocalModPendingActionStore.SaveLocalModPendingActions();
        }

        public static int GetPendingRestartLocalModCount() {
            int count = 0;
            // ModListAll 主线程排序时 Clear+AddRange 不可并发枚举，跨线程读必须封送
            Dispatcher.Dispatch(
                delegate {
                    foreach (ModEntity entity in ModsManager.ModListAll) {
                        if (entity?.IsPendingRestart == true) {
                            count++;
                        }
                    }
                },
                true
            );
            return count;
        }

        public static bool HasPendingLocalModCompatibilityOverride(string packageName, string filePathOrName, string version) =>
            s_compatibilityOverrides.Matches(packageName, filePathOrName, version);

        // 下载前已经判定为兼容的版本，在“已下载但未重启”阶段沿用这份判断，避免重新扫本地包后出现前后提示不一致。
        public static void RememberPendingLocalModCompatibilityOverride(string packageName, string fileName, PostVersionItem version) {
            if (version == null) {
                return;
            }
            bool isCompatible = ModCompatibility.IsCompatibleWithCurrentVersion(
                version,
                VersionCatalogCache.CurrentApiVersion,
                VersionCatalogCache.CurrentGameVersion,
                VersionCatalogCache.CachedGameVersionsById,
                VersionCatalogCache.CachedVersionCatalog
            );
            if (!isCompatible) {
                return;
            }
            string versionText = !string.IsNullOrWhiteSpace(version.Version)
                ? version.Version
                : version.Files?.FirstOrDefault(file => file != null)?.PackageVersion;
            s_compatibilityOverrides.Remember(packageName, fileName, versionText);
        }

        public static string FindExactLocalDownloadFilePath(ExternalContentType type, string fileName) =>
            LocalModInstallStatus.FindExactLocalDownloadFilePath(type, fileName);

        public static List<LocalResourceItem> GetCommunityLocalModSnapshot() => LocalModInstallStatus.GetCommunityLocalModSnapshot();

        public static int GetCachedLocalModPostId(LocalResourceItem resourceItem) => LocalModReviewState.GetCachedLocalModPostId(resourceItem);

        public static void RememberLocalModPostId(string importedName, string fileName, int postId) {
            LocalModReviewState.RememberLocalModPostId(importedName, fileName, postId);
        }

        public static void RememberLocalPostId(string resourceKey, string displayName, string fileName, int postId) {
            LocalModReviewState.RememberLocalPostId(resourceKey, displayName, fileName, postId);
        }

        public static bool IsInstalledCommunityModVersion(PostVersionItem version) => LocalModInstallStatus.IsInstalledCommunityModVersion(version);

        public static bool IsInstalledCommunityModVersion(PostVersionItem version, List<LocalResourceItem> communityLocalMods) =>
            LocalModInstallStatus.IsInstalledCommunityModVersion(version, communityLocalMods);

        public static Dictionary<string, LocalModCommunityStatusInfo> LocalModCommunityStatusCache =>
            LocalModInstallStatus.LocalModCommunityStatusCache;

        public static Dictionary<string, LocalModUpdateCheckResult> LocalModUpdateDetailsCache => LocalModInstallStatus.LocalModUpdateDetailsCache;
    }
}