using Engine;

namespace Game
{
    /// <summary>
    /// 集中维护 ScbbsPlus 的本地存储路径，避免业务代码重复拼接用户目录。
    /// </summary>
    internal static class CommunityStoragePaths
    {
        public static readonly string RootDirectory = Storage.CombinePaths(ModsManager.DocPath, "Community");

        public static readonly string StateFile = Storage.CombinePaths(RootDirectory, "state.json");
        public static readonly string LocalModReviewsFile = Storage.CombinePaths(RootDirectory, "local-mod-reviews.json");
        public static readonly string LocalModPendingActionsFile = Storage.CombinePaths(RootDirectory, "local-mod-pending-actions.json");
        public static readonly string LocalModpackStateFile = Storage.CombinePaths(RootDirectory, "local-integration-pack-state.json");
        public static readonly string PlayedTimeFile = Storage.CombinePaths(RootDirectory, "playedtime.json");
        public static readonly string DownloadQueueFile = Storage.CombinePaths(RootDirectory, "download-queue.json");
        public static readonly string AuthDirectory = Storage.CombinePaths(RootDirectory, "auth");
        public static readonly string DownloadCacheDirectory = Storage.CombinePaths(RootDirectory, "download-cache");
        public static readonly string ModpackBackupsDirectory = Storage.CombinePaths(RootDirectory, "integration-pack-backups");
    }
}
