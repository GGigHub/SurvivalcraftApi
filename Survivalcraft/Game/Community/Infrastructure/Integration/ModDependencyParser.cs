using NuGet.Versioning;

namespace Game {
    static class ModDependencyParser {
        public static IReadOnlyList<ModDependencyInfo> GetDependencies(ModEntity entity) => GetDependencies(entity?.modInfo);

        public static IReadOnlyList<ModDependencyInfo> GetDependencies(ModInfo modInfo) {
            if (modInfo?.DependencyRanges == null) {
                return [];
            }
            return modInfo.DependencyRanges.Select(dependency => CreateDependencyInfo(dependency.Key, dependency.Value)).ToArray();
        }

        // DependencyRanges 的值由引擎解析保证非 null（解析失败不入典），无字符串/空值兜底分支。
        static ModDependencyInfo CreateDependencyInfo(string packageName, VersionRange versionRange) {
            string rangeText = versionRange.ToString()?.Trim();
            bool isAllVersions = versionRange.Equals(VersionRange.All) || string.IsNullOrWhiteSpace(rangeText);
            return isAllVersions
                ? new ModDependencyInfo(packageName, null)
                : new ModDependencyInfo(packageName, rangeText, versionRange: versionRange);
        }
    }
}