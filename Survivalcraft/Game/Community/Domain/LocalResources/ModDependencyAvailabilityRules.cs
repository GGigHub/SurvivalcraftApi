using NuGet.Versioning;

namespace Game {
    enum ModDependencyAvailability {
        Missing,
        Incompatible,
        PendingRestart,
        Loaded
    }

    static class ModDependencyAvailabilityRules {
        public static ModDependencyAvailability Evaluate(ModDependencyInfo dependency,
            ISet<string> loadedPackages,
            ISet<string> pendingPackages,
            ISet<string> incompatiblePackages) {
            if (IsInstalledModMatching(dependency, loadedPackages)) {
                return ModDependencyAvailability.Loaded;
            }
            if (IsInstalledModMatching(dependency, pendingPackages)) {
                return ModDependencyAvailability.PendingRestart;
            }
            if (IsInstalledModMatching(dependency, incompatiblePackages)) {
                return ModDependencyAvailability.Incompatible;
            }
            return ModDependencyAvailability.Missing;
        }

        public static string MakePackageVersionKey(string packageName, string version) => $"{packageName?.Trim()}\n{version?.Trim()}";

        static bool IsInstalledModMatching(ModDependencyInfo dependency, ISet<string> installedPackages) {
            if (dependency == null
                || string.IsNullOrWhiteSpace(dependency.PackageName)
                || installedPackages == null) {
                return false;
            }
            string packageName = dependency.PackageName.Trim();
            if (string.IsNullOrWhiteSpace(dependency.VersionRangeString)) {
                return installedPackages.Contains(packageName);
            }
            if (installedPackages.Contains(MakePackageVersionKey(packageName, dependency.VersionRangeString))) {
                return true;
            }

            // 无引擎解析产物即无有效范围声明，判不满足；声明文本不再二次解析。
            if (dependency.VersionRange == null) {
                return false;
            }
            string prefix = $"{packageName}\n";
            foreach (string installedPackage in installedPackages) {
                if (string.IsNullOrWhiteSpace(installedPackage)
                    || !installedPackage.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }
                string installedVersion = installedPackage.Substring(prefix.Length).Trim();
                if (!ExternalVersionParser.TryParse(installedVersion, out NuGetVersion installed)) {
                    continue;
                }
                if (dependency.VersionRange.Satisfies(installed)) {
                    return true;
                }
            }
            return false;
        }
    }
}