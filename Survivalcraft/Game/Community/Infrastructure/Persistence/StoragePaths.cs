using Engine;

namespace Game
{
    /// <summary>
    /// 集中维护 ScbbsPlus 的本地存储路径，避免业务代码重复拼接用户目录。
    /// </summary>
    internal static class StoragePaths
    {
        public static string RootDirectory { get; } = "data:ScbbsPlus";

        public static string AuthFile { get; } = Storage.CombinePaths(RootDirectory, "auth.bin");
        public static string StateFile { get; } = Storage.CombinePaths(RootDirectory, "state.json");
        public static string LocalModReviewsFile { get; } = Storage.CombinePaths(RootDirectory, "local-mod-reviews.json");
        public static string LocalModPendingActionsFile { get; } = Storage.CombinePaths(RootDirectory, "local-mod-pending-actions.json");
        public static string LocalModpackStateFile { get; } = Storage.CombinePaths(RootDirectory, "local-integration-pack-state.json");
        public static string PlayedTimeFile { get; } = Storage.CombinePaths(RootDirectory, "playedtime.json");
        public static string DownloadQueueFile { get; } = Storage.CombinePaths(RootDirectory, "download-queue.json");
        public static string DownloadCacheDirectory { get; } = Storage.CombinePaths(RootDirectory, "download-cache");
        public static string ModpackBackupsDirectory { get; } = Storage.CombinePaths(RootDirectory, "integration-pack-backups");
    }
}
