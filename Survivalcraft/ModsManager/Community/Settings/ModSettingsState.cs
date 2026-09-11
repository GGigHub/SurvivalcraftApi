using System.Globalization;
using System.Xml.Linq;
using Engine;

namespace Game
{
    /// <summary>
    /// 不应暴露在原生设置页面中的模组流程状态，随游戏 ModSettings.xml 保存。
    /// setter 只写内存，落盘交给游戏本体的自动保存（Window.Deactivated/重启前的
    /// ClosedHandler → SettingsManager.SaveSettings；游戏内定期存档；模组管理重启）。
    /// </summary>
    internal static class ModSettingsState
    {
        private const string ElementName = "ScbbsPlusState";
        private static bool s_loaded;
        private static bool s_firstDownloadSelfReviewPromptShown;
        private static string s_restartReturnScreen;
        private static ScbbsPlusCommunityFileType s_localFileType = ScbbsPlusCommunityFileType.Mod;
        private static ScbbsPlusCommunityFileType s_communityFileType = ScbbsPlusCommunityFileType.Mod;
        private static CommunityOrderType s_communityOrderType = CommunityOrderType.Hot;
        private static bool s_communityOnlyCurrentVersion = true;
        private static int s_communitySection = 1;
        private static ScbbsPlusCommunityFileType s_myPublishedFileType = ScbbsPlusCommunityFileType.Mod;

        public static bool FirstDownloadSelfReviewPromptShown => s_firstDownloadSelfReviewPromptShown;
        public static ScbbsPlusCommunityFileType LocalFileType => s_localFileType;
        public static ScbbsPlusCommunityFileType CommunityFileType => s_communityFileType;
        public static CommunityOrderType CommunityOrderType => s_communityOrderType;
        public static bool CommunityOnlyCurrentVersion => s_communityOnlyCurrentVersion;
        public static int CommunitySection => s_communitySection;
        public static ScbbsPlusCommunityFileType MyPublishedFileType => s_myPublishedFileType;

        public static void Load(XElement modElement)
        {
            if (s_loaded)
            {
                return;
            }

            s_loaded = true;
            XElement state = modElement?.Element(ElementName);
            if (state == null)
            {
                return;
            }

            s_firstDownloadSelfReviewPromptShown = ReadBool(state, "FirstDownloadSelfReviewPromptShown", false);
            s_localFileType = CommunityFileTypes.FromSettingValue(ReadInt(state, "LocalFileType", 5));
            s_communityFileType = CommunityFileTypes.FromSettingValue(ReadInt(state, "CommunityFileType", 5));
            // 旧版本存字符串 "1"-"4"，ReadInt 的 int.TryParse 直接兼容
            s_communityOrderType = CommunityFileTypes.OrderFromSettingValue(ReadInt(state, "CommunityOrderType", 4));
            s_communityOnlyCurrentVersion = ReadBool(state, "CommunityOnlyCurrentVersion", true);
            s_communitySection = NormalizeSection(ReadInt(state, "CommunitySection", 1));
            s_myPublishedFileType = CommunityFileTypes.FromSettingValue(ReadInt(state, "MyPublishedFileType", 5));

            string screen = (string)state.Attribute("RestartReturnScreen");
            if (ScreenNames.IsKnown(screen))
            {
                s_restartReturnScreen = screen;
            }
        }

        public static void Save(XElement modElement)
        {
            XElement state = new(ElementName);
            state.SetAttributeValue("FirstDownloadSelfReviewPromptShown", s_firstDownloadSelfReviewPromptShown);
            state.SetAttributeValue("LocalFileType", (int)s_localFileType);
            state.SetAttributeValue("CommunityFileType", (int)s_communityFileType);
            state.SetAttributeValue("CommunityOrderType", (int)s_communityOrderType);
            state.SetAttributeValue("CommunityOnlyCurrentVersion", s_communityOnlyCurrentVersion);
            state.SetAttributeValue("CommunitySection", s_communitySection);
            state.SetAttributeValue("MyPublishedFileType", (int)s_myPublishedFileType);
            if (s_restartReturnScreen != null)
            {
                state.SetAttributeValue("RestartReturnScreen", s_restartReturnScreen);
            }
            modElement.Add(state);
        }

        public static void SetFirstDownloadSelfReviewPromptShown()
        {
            s_firstDownloadSelfReviewPromptShown = true;
        }

        public static bool HasPendingRestartReturnTarget() => s_restartReturnScreen != null;

        public static bool TryGetRestartReturnTarget(out string screenName)
        {
            screenName = s_restartReturnScreen;
            return ScreenNames.IsKnown(screenName);
        }

        public static void SetRestartReturnTarget(string screenName)
        {
            if (!ScreenNames.IsKnown(screenName))
            {
                Log.Warning(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusModSettingsState", "RejectInvalidRestartTarget"), screenName));
                return;
            }
            s_restartReturnScreen = screenName;
        }

        public static void ClearRestartReturnTarget()
        {
            s_restartReturnScreen = null;
        }

        public static bool TryGetScreenFileType(string screenName, out ScbbsPlusCommunityFileType fileType)
        {
            fileType = screenName switch
            {
                ScreenNames.Local => s_localFileType,
                ScreenNames.Community => s_communityFileType,
                ScreenNames.MyPublished => s_myPublishedFileType,
                _ => ScbbsPlusCommunityFileType.Unknown
            };
            return ScreenNames.IsKnown(screenName);
        }

        public static void SetScreenFileType(string screenName, ScbbsPlusCommunityFileType fileType)
        {
            switch (screenName)
            {
                case ScreenNames.Local:
                    s_localFileType = fileType;
                    break;
                case ScreenNames.Community:
                    s_communityFileType = fileType;
                    break;
                case ScreenNames.MyPublished:
                    s_myPublishedFileType = fileType;
                    break;
                default:
                    return;
            }
        }

        public static void SetCommunityDefaults(
            ScbbsPlusCommunityFileType fileType,
            CommunityOrderType orderType,
            bool onlyCurrentVersion,
            int section)
        {
            s_communityFileType = fileType;
            s_communityOrderType = orderType;
            s_communityOnlyCurrentVersion = onlyCurrentVersion;
            s_communitySection = NormalizeSection(section);
        }

        private static int ReadInt(XElement element, string name, int fallback)
        {
            return int.TryParse((string)element.Attribute(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : fallback;
        }

        private static bool ReadBool(XElement element, string name, bool fallback)
        {
            return bool.TryParse((string)element.Attribute(name), out bool value) ? value : fallback;
        }

        private static int NormalizeSection(int section) => section switch
        {
            2 or 3 => section,
            _ => 1
        };
    }
}
