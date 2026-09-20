using System;
using Engine;

namespace Game
{
    /// <summary>
    /// 不应暴露在原生设置页面中的模组流程状态，随 data:ScbbsPlus/state.json 以 JSON 持久化
    /// （JsonFileStore 临时文件原子替换写）。读取走 EnsureLoaded 惰性门闩（首次访问任意
    /// 公开成员时读入）；写方法改完内存后同步落盘（Persist）——变更低频（用户改筛选），
    /// 同步写盘可接受。旧 ModSettings.xml 内的 ScbbsPlusState 元素不再迁移，
    /// 升级后首次运行为全默认。重启直达目标已改由 ScreensManager.RestartGameToScreen
    /// 经 Settings.xml 承载，不再入 state.json。
    /// </summary>
    internal static class ModSettingsState
    {
        private static bool s_loaded;
        private static bool s_firstDownloadSelfReviewPromptShown;
        private static CommunityFileType s_localFileType = CommunityFileType.Mod;
        private static CommunityFileType s_communityFileType = CommunityFileType.Mod;
        private static CommunityOrderType s_communityOrderType = CommunityOrderType.Hot;
        private static bool s_communityOnlyCurrentVersion = true;
        private static int s_communitySection = 1;
        private static CommunityFileType s_myPublishedFileType = CommunityFileType.Mod;

        public static bool FirstDownloadSelfReviewPromptShown
        {
            get { EnsureLoaded(); return s_firstDownloadSelfReviewPromptShown; }
        }

        public static CommunityFileType LocalFileType
        {
            get { EnsureLoaded(); return s_localFileType; }
        }

        public static CommunityFileType CommunityFileType
        {
            get { EnsureLoaded(); return s_communityFileType; }
        }

        public static CommunityOrderType CommunityOrderType
        {
            get { EnsureLoaded(); return s_communityOrderType; }
        }

        public static bool CommunityOnlyCurrentVersion
        {
            get { EnsureLoaded(); return s_communityOnlyCurrentVersion; }
        }

        public static int CommunitySection
        {
            get { EnsureLoaded(); return s_communitySection; }
        }

        public static CommunityFileType MyPublishedFileType
        {
            get { EnsureLoaded(); return s_myPublishedFileType; }
        }

        // 我的发布屏文件类型写入端（模组 SetScreenFileType 按屏名版式在游戏内收敛为本屏单方法）。
        public static void SetMyPublishedFileType(CommunityFileType fileType)
        {
            EnsureLoaded();
            s_myPublishedFileType = fileType;
            Persist();
        }

        public static void SetFirstDownloadSelfReviewPromptShown()
        {
            EnsureLoaded();
            s_firstDownloadSelfReviewPromptShown = true;
            Persist();
        }

        public static void SetCommunityDefaults(
            CommunityFileType fileType,
            CommunityOrderType orderType,
            bool onlyCurrentVersion,
            int section)
        {
            EnsureLoaded();
            s_communityFileType = fileType;
            s_communityOrderType = orderType;
            s_communityOnlyCurrentVersion = onlyCurrentVersion;
            s_communitySection = NormalizeSection(section);
            Persist();
        }

        // 文件不存在经 FileExists 短路，按全默认处理（省一次 FileNotFoundException 往返与告警）；
        // 文件存在但读失败时 JsonFileStore.Read 记 Log.Warning 并返回 null，同样按全默认处理。
        private static void EnsureLoaded()
        {
            if (s_loaded || !Storage.FileExists(CommunityStoragePaths.StateFile))
            {
                return;
            }

            s_loaded = true;
            StateData state = JsonFileStore.Read<StateData>(CommunityStoragePaths.StateFile);
            if (state == null)
            {
                return;
            }

            s_firstDownloadSelfReviewPromptShown = state.FirstDownloadSelfReviewPromptShown;
            s_localFileType = CommunityFileTypes.FromSettingValue(state.LocalFileType);
            s_communityFileType = CommunityFileTypes.FromSettingValue(state.CommunityFileType);
            s_communityOrderType = CommunityFileTypes.OrderFromSettingValue(state.CommunityOrderType);
            s_communityOnlyCurrentVersion = state.CommunityOnlyCurrentVersion;
            s_communitySection = NormalizeSection(state.CommunitySection);
            s_myPublishedFileType = CommunityFileTypes.FromSettingValue(state.MyPublishedFileType);
        }

        private static void Persist()
        {
            try
            {
                JsonFileStore.Write(CommunityStoragePaths.StateFile, new StateData
                {
                FirstDownloadSelfReviewPromptShown = s_firstDownloadSelfReviewPromptShown,
                LocalFileType = (int)s_localFileType,
                    CommunityFileType = (int)s_communityFileType,
                    CommunityOrderType = (int)s_communityOrderType,
                    CommunityOnlyCurrentVersion = s_communityOnlyCurrentVersion,
                    CommunitySection = s_communitySection,
                    MyPublishedFileType = (int)s_myPublishedFileType
                });
            }
            catch (Exception ex)
            {
                Log.Warning($"[ScbbsPlus] {ex.Message}");
            }
        }

        // 数值初值对应旧 XElement.ReadInt 的回退值（文件类型 5、排序 4、节 1）：
        // JSON 缺属性时退化语义与旧 XML 缺属性一致。
        private sealed class StateData
        {
            public bool FirstDownloadSelfReviewPromptShown { get; set; }
            public int LocalFileType { get; set; } = 5;
            public int CommunityFileType { get; set; } = 5;
            public int CommunityOrderType { get; set; } = 4;
            public bool CommunityOnlyCurrentVersion { get; set; } = true;
            public int CommunitySection { get; set; } = 1;
            public int MyPublishedFileType { get; set; } = 5;
        }

        private static int NormalizeSection(int section) => section switch
        {
            2 or 3 => section,
            _ => 1
        };
    }
}
