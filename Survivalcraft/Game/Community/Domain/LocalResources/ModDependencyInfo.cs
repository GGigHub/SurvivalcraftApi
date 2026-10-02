using NuGet.Versioning;

namespace Game {
    sealed class ModDependencyInfo {
        public ModDependencyInfo(string packageName, string version, string displayName = null, VersionRange versionRange = null, string link = null) {
            PackageName = packageName?.Trim();
            VersionRangeString = version?.Trim();
            DisplayName = displayName?.Trim();
            VersionRange = versionRange;
            Link = link?.Trim();
        }

        public string PackageName { get; }

        // 依赖声明的规范化文本：展示、服务器查询、持久化 key 用。
        public string VersionRangeString { get; }

        // 引擎解析产物（mod 声明链携带；世界存档等纯文本来源为 null）：满足判定直接 Satisfies。
        public VersionRange VersionRange { get; }

        public string DisplayName { get; }

        // 依赖声明的下载页/主页链接（modinfo.json 富写法才有；世界存档等纯文本来源为 null）
        public string Link { get; }
    }
}