namespace Game {
    /// <summary>
    /// 下载源（镜像线路）注册表，结构镜像 CommunityServerManager：
    /// 内置只读线路 + 用户自建列表 + 当前默认线路，经 SettingsManager 字符串属性持久化。
    /// </summary>
    /// <remarks>
    /// 身份即名称（<see cref="Info.Name" />，Ordinal 比较）：队列文件与设置按名称恢复，
    /// 因此内置线路的内部名沿用旧枚举名（DefaultCn/Source1Global…），旧队列文件里的
    /// "Source": "Source1Global" 字符串可直接按名命中，无需迁移。内置名保留，用户条目不得占用。
    /// </remarks>
    public static class CommunityDownloadSourceManager {
        public class Info : IEquatable<Info> {
            public string Name;
            public string Host;

            public Info(string name, string host) {
                Name = name;
                Host = host;
            }

            public bool Equals(Info other) {
                if (other is null) {
                    return false;
                }
                if (ReferenceEquals(this, other)) {
                    return true;
                }
                return string.Equals(Name, other.Name, StringComparison.Ordinal) && string.Equals(Host, other.Host, StringComparison.Ordinal);
            }

            public override string ToString() => $"{Name}^^{Host}";

            public static Info FromString(string str) {
                string[] parts = str.Split("^^");
                return parts.Length == 2 ? new Info(parts[0], parts[1]) : null;
            }

            public Info Clone() => new(Name, Host);
        }

        // 内置线路（只读，可克隆不可改删）。DefaultCn 的 Host 为空 = 不改写 URL（原站直连）。
        public static readonly Info DefaultCnInfo = new("DefaultCn", string.Empty);
        public static readonly Info Source1GlobalInfo = new("Source1Global", "r2.scbbs.top");
        public static readonly Info Source2CnInfo = new("Source2Cn", "img.suancaixianyu.cn");
        public static readonly Info Source3GlobalInfo = new("Source3Global", "r2img.ccxk.top");
        public static readonly Info Source4GlobalInfo = new("Source4Global", "r2.ccxk.top");

        static readonly Info[] DefaultInfos = [DefaultCnInfo, Source1GlobalInfo, Source2CnInfo, Source3GlobalInfo, Source4GlobalInfo];

        public static List<Info> UserInfos = [];

        /// <summary>当前默认线路：仅 CustomDefault 选择模式与 CommunityDefaultDownloadSourceName 设置消费。</summary>
        public static Info CurrentDefaultInfo = DefaultCnInfo;

        // 设置加载顺序不保证（LoadSettings 按 Settings.xml 键序逐键赋值）：默认源名可能先于
        // 自建列表到达。先记待解析名，UserInfos 载入完成后重放；始终无命中（条目已删除）则维持现状。
        static string s_pendingDefaultName;

        public static void SetPendingDefaultName(string name) {
            Info resolved = FindByName(name);
            if (resolved != null) {
                CurrentDefaultInfo = resolved;
                s_pendingDefaultName = null;
            }
            else {
                s_pendingDefaultName = name;
            }
        }

        public static void ApplyPendingDefaultName() {
            if (s_pendingDefaultName != null) {
                SetPendingDefaultName(s_pendingDefaultName);
            }
        }

        /// <summary>线路显示名：内置线路用本地化名，自建线路用用户命名。</summary>
        public static string GetDisplayName(Info info) {
            if (info == null) {
                return string.Empty;
            }
            if (info == DefaultCnInfo) {
                return LanguageControl.Get("CommunityDownloadSource", "DefaultCn");
            }
            if (info == Source1GlobalInfo) {
                return LanguageControl.Get("CommunityDownloadSource", "Source1Global");
            }
            if (info == Source2CnInfo) {
                return LanguageControl.Get("CommunityDownloadSource", "Source2Cn");
            }
            if (info == Source3GlobalInfo) {
                return LanguageControl.Get("CommunityDownloadSource", "Source3Global");
            }
            return info == Source4GlobalInfo ? LanguageControl.Get("CommunityDownloadSource", "Source4Global") : info.Name;
        }

        public static bool IsDefaultInfo(Info info) => Array.IndexOf(DefaultInfos, info) >= 0;

        public static IEnumerable<Info> GetAllInfos() {
            foreach (Info info in DefaultInfos) {
                yield return info;
            }
            foreach (Info info in UserInfos) {
                yield return info;
            }
        }

        public static void ChangeDefaultInfo(Info info) {
            if (info != null
                && CurrentDefaultInfo != info) {
                CurrentDefaultInfo = info;
            }
        }

        public static void AddUserInfo(Info info) {
            if (info != null) {
                UserInfos.Add(info);
            }
        }

        public static void RemoveUserInfo(Info info) {
            if (info != null
                && UserInfos.Remove(info)
                && CurrentDefaultInfo == info) {
                // 删除的恰好是默认线路时回退原站直连，避免悬空引用
                CurrentDefaultInfo = DefaultCnInfo;
            }
        }

        /// <summary>按名称查线路（Ordinal）；内置线路优先，未命中返回 null。</summary>
        public static Info FindByName(string name) {
            foreach (Info info in DefaultInfos) {
                if (string.Equals(info.Name, name, StringComparison.Ordinal)) {
                    return info;
                }
            }
            foreach (Info info in UserInfos) {
                if (string.Equals(info.Name, name, StringComparison.Ordinal)) {
                    return info;
                }
            }
            return null;
        }

        /// <summary>名称查重：内置内部名是保留身份，用户条目间亦不得重名（FindByName 以名称为身份）。except 用于原地编辑时豁免自身。</summary>
        public static bool NameExists(string name, Info except = null) {
            if (string.IsNullOrEmpty(name)) {
                return false;
            }
            foreach (Info info in GetAllInfos()) {
                if (!ReferenceEquals(info, except)
                    && string.Equals(info.Name, name, StringComparison.Ordinal)) {
                    return true;
                }
            }
            return false;
        }
    }
}