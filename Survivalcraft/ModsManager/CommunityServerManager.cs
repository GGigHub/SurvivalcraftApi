namespace Game {
    public static class CommunityServerManager {
        public enum Type {
            Original = 0,
            Chinese = 1
        }

        public class Info : IEquatable<Info> {
            public Type Type;
            public string Name;
            public string ApiUrl;
            public string WebsiteUrl;

            public Info(Type type, string name, string apiUrl, string websiteUrl = null) {
                Type = type;
                Name = name;
                ApiUrl = apiUrl;
                WebsiteUrl = websiteUrl;
            }

            public bool Equals(Info other) {
                if (other is null) {
                    return false;
                }
                if (ReferenceEquals(this, other)) {
                    return true;
                }
                return Type == other.Type && Name == other.Name && ApiUrl == other.ApiUrl && WebsiteUrl == other.WebsiteUrl;
            }

            public override string ToString() {
                return $"{(int)Type}^^{Name}^^{ApiUrl}^^{WebsiteUrl}";
            }

            public static Info FromString(string str) {
                string[] parts = str.Split("^^");
                if (parts.Length == 4 && int.TryParse(parts[0], out int type)) {
                    return new Info((Type)type, parts[1], parts[2], parts[3]);
                }
                return null;
            }

            public Info Clone() => new(Type, Name, ApiUrl, WebsiteUrl);
        }
#if BROWSER
        public static Info DefaultOriginalInfo = new(Type.Original, string.Empty, "https://cloudflare-cors-anywhere.weathered-shadow-6c41.workers.dev/?https://scresdir.appspot.com/resource");
        public static Info DefaultChineseInfo = new(Type.Chinese, string.Empty, "https://api.survivalcraft.cn/api", "https://test.suancaixianyu.cn/#/modList/0");
#else
        public static Info DefaultOriginalInfo = new(Type.Original, string.Empty, "https://scresdir.appspot.com/resource");
        public static Info DefaultChineseInfo = new(Type.Chinese, string.Empty, "https://api.survivalcraft.cn/api", "https://test.suancaixianyu.cn/#/modList/0");
#endif

        public static List<Info> UserInfos = [];
        public static Info CurrentOriginalInfo = DefaultOriginalInfo;
        public static Info CurrentChineseInfo = DefaultChineseInfo;

        /// <summary>默认社区 API 基址（fallback 常量）：归一化的 DefaultChineseInfo.ApiUrl。</summary>
        public static string DefaultApiBaseUrl => NormalizeApiBaseUrl(DefaultChineseInfo.ApiUrl);

        /// <summary>
        /// 当前社区 API 基址：全游戏唯一读取口（新栈 HTTP 客户端与旧内容栈共用）。
        /// 归一化保证结果要么是原绝对 URL，要么回退默认服务器——绝不产出相对 URL
        /// （HttpClient 收到相对 URL 会直接抛 InvalidRequestUri）。
        /// </summary>
        public static string CurrentApiBaseUrl => NormalizeApiBaseUrl(CurrentChineseInfo.ApiUrl);

        /// <summary>归一化：去尾斜杠；空值回退默认服务器。</summary>
        public static string NormalizeApiBaseUrl(string value)
        {
            string normalized = (value ?? string.Empty).Trim();
            while (normalized.EndsWith("/", StringComparison.Ordinal))
            {
                normalized = normalized[..^1];
            }

            return normalized.Length == 0 ? DefaultApiBaseUrl : normalized;
        }

        /// <summary>服务器条目显示名：内置条目用本地化名，自建条目用用户命名。</summary>
        public static string GetDisplayName(Info info) {
            if (info == null) {
                return string.Empty;
            }
            if (info == DefaultOriginalInfo) {
                return LanguageControl.Get("ContentScreen", "CommunityType", "OriginalCommunity");
            }
            if (info == DefaultChineseInfo) {
                return LanguageControl.Get("ContentScreen", "CommunityType", "ChineseCommunity");
            }
            return info.Name;
        }

        public static IEnumerable<Info> GetAllInfos() {
            if (SettingsManager.OriginalCommunityContentMode == CommunityContentMode.Disabled) {
                if (SettingsManager.CommunityContentMode != CommunityContentMode.Disabled) {
                    yield return DefaultChineseInfo;
                }
            }
            else if (SettingsManager.CommunityContentMode == CommunityContentMode.Disabled) {
                yield return DefaultOriginalInfo;
            }
            else {
                yield return DefaultOriginalInfo;
                yield return DefaultChineseInfo;
            }
            foreach (Info info in UserInfos) {
                yield return info;
            }
        }

        public static void ChangeOriginalInfo(Info info) {
            if (CurrentOriginalInfo != info && info.Type == Type.Original) {
                OriginalCommunityContentManager.m_feedbackCache.Clear();
                OriginalCommunityContentManager.m_idToAddressMap.Clear();
                CurrentOriginalInfo = info;
            }
        }

        public static void ChangeChineseInfo(Info info) {
            if (CurrentChineseInfo != info && info.Type == Type.Chinese) {
                CommunityContentManager.m_feedbackCache.Clear();
                CommunityContentManager.m_idToAddressMap.Clear();
                // 切服不登出：认证由 AuthSessionManager per-URL 会话承载（OnServerSwitched
                // 激活目标服会话，各服独立保存恢复）；ApiBaseUrl 派生自 CurrentChineseInfo，
                // 此处是唯一写点，无需经设置层事件绕行。
                CurrentChineseInfo = info;
                AuthSessionManager.Instance.OnServerSwitched();
            }
        }
    }
}