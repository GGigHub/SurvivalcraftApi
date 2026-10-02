using NuGet.Versioning;

namespace Game {
    static class ModDependencyParser {
        public static IReadOnlyList<ModDependencyInfo> GetDependencies(ModEntity entity) => GetDependencies(entity?.modInfo);

        public static IReadOnlyList<ModDependencyInfo> GetDependencies(ModInfo modInfo) {
            return modInfo?.DependencyRanges == null
                ? []
                : [.. modInfo.DependencyRanges.Select(dependency => CreateDependencyInfo(dependency.Key, dependency.Value, modInfo.DependencyDetails))];
        }

        // DependencyRanges 的值由引擎解析保证非 null（解析失败不入典），无字符串/空值兜底分支。
        // 富信息（友好名/链接）从 DependencyDetails 旁路字典取，键恒存在（解析时保证是 Ranges 键的子集）。
        static ModDependencyInfo CreateDependencyInfo(string packageName, VersionRange versionRange, Dictionary<string, ModDependencyDetail> details = null) {
            string rangeText = versionRange.ToString()?.Trim();
            bool isAllVersions = versionRange.Equals(VersionRange.All) || string.IsNullOrWhiteSpace(rangeText);
            ModDependencyDetail detail = details?.GetValueOrDefault(packageName);
            return isAllVersions
                ? new ModDependencyInfo(packageName, null, displayName: detail?.Name, link: detail?.Link)
                : new ModDependencyInfo(packageName, rangeText, displayName: detail?.Name, versionRange: versionRange, link: detail?.Link);
        }
    }
}