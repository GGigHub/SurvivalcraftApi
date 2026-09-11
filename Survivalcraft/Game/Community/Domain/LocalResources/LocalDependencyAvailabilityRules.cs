using System;
using System.Collections.Generic;
using NuGet.Versioning;

namespace Game
{
    internal enum LocalDependencyAvailability
    {
        Missing,
        Incompatible,
        PendingRestart,
        Loaded
    }

    internal static class LocalDependencyAvailabilityRules
    {
        public static LocalDependencyAvailability Evaluate(
            ModDependencyInfo dependency,
            ISet<string> loadedPackages,
            ISet<string> pendingPackages,
            ISet<string> incompatiblePackages)
        {
            if (IsInstalledModMatching(dependency, loadedPackages))
            {
                return LocalDependencyAvailability.Loaded;
            }

            if (IsInstalledModMatching(dependency, pendingPackages))
            {
                return LocalDependencyAvailability.PendingRestart;
            }

            if (IsInstalledModMatching(dependency, incompatiblePackages))
            {
                return LocalDependencyAvailability.Incompatible;
            }

            return LocalDependencyAvailability.Missing;
        }

        public static string MakePackageVersionKey(string packageName, string version)
        {
            return $"{packageName?.Trim()}\n{version?.Trim()}";
        }

        private static bool IsInstalledModMatching(ModDependencyInfo dependency, ISet<string> installedPackages)
        {
            if (dependency == null || string.IsNullOrWhiteSpace(dependency.PackageName) || installedPackages == null)
            {
                return false;
            }

            string packageName = dependency.PackageName.Trim();
            if (string.IsNullOrWhiteSpace(dependency.VersionRangeString))
            {
                return installedPackages.Contains(packageName);
            }

            if (installedPackages.Contains(MakePackageVersionKey(packageName, dependency.VersionRangeString)))
            {
                return true;
            }

            // 无引擎解析产物即无有效范围声明，判不满足；声明文本不再二次解析。
            if (dependency.VersionRange == null)
            {
                return false;
            }

            string prefix = $"{packageName}\n";
            foreach (string installedPackage in installedPackages)
            {
                if (string.IsNullOrWhiteSpace(installedPackage)
                    || !installedPackage.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string installedVersion = installedPackage.Substring(prefix.Length).Trim();
                if (!ExternalVersionParser.TryParse(installedVersion, out NuGetVersion installed))
                {
                    continue;
                }

                if (dependency.VersionRange.Satisfies(installed))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
