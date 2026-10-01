using Engine.Graphics;

namespace Game {
    /// <summary>
    /// 本地资源子系统的核心数据模型（主报告 #1 归位：原住 Presentation/Models，
    /// 而 Domain/Infrastructure 才是它的主要消费方，反向依赖）。
    /// </summary>
    public class LocalResourceItem {
        public ExternalContentType Type { get; set; }
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public DateTime CreationTime { get; set; }
        public int UseCount { get; set; }
        public bool IsBuiltIn { get; set; }
        public Texture2D Texture { get; set; }
        public ModEntity ModEntity { get; set; }
        public string LocalFilePath { get; set; }
        public string PackageName { get; set; }
        public string Version { get; set; }
        public bool RequiresRestartToLoad { get; set; }
        public bool IsIncompatibleWithCurrentGame { get; set; }
        public bool IsDisabled { get; set; }
        public bool HasDuplicatePackageName { get; set; }
        public ModDisableReason DisableReason { get; set; }
        public LocalModPendingActionType PendingAction { get; set; }
        public LocalModCommunityStatusInfo CommunityStatus { get; set; }
        public object Tag { get; set; }
    }

    public class LocalModCommunityStatusInfo {
        public bool CheckCompleted { get; set; }
        public bool IsPublished { get; set; }
        public int PostId { get; set; }
        public string LatestVersion { get; set; }
        public bool HasUpdate { get; set; }
    }
}