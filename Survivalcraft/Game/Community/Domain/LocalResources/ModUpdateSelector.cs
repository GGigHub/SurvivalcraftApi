using System;
using System.Collections.Generic;
using System.Globalization;
using NuGet.Versioning;

namespace Game
{
    internal static class ModUpdateSelector
    {
        public static PostVersionItem FindLatestVersion(
            IReadOnlyList<PostVersionItem> versions,
            string expectedPackageName = null,
            string preferredFileName = null,
            Func<PostVersionItem, bool> compatibilityPredicate = null)
        {
            if (versions == null)
            {
                return null;
            }

            PostVersionItem best = null;
            foreach (PostVersionItem version in versions)
            {
                if (!IsModVersion(version)
                    || FindBestFile(version, expectedPackageName, preferredFileName) == null
                    || (compatibilityPredicate != null && !compatibilityPredicate(version)))
                {
                    continue;
                }

                if (best == null || CompareVersions(version, best) > 0)
                {
                    best = version;
                }
            }
            return best;
        }

        public static PostFileItem FindBestFile(
            PostVersionItem version,
            string expectedPackageName = null,
            string preferredFileName = null)
        {
            if (version?.Files == null)
            {
                return null;
            }

            bool requirePackageMatch = !string.IsNullOrWhiteSpace(expectedPackageName);
            bool hasAnyPackageMetadata = false;
            PostFileItem matchingFileNameScmod = null;
            PostFileItem matchingPackageFallback = null;
            PostFileItem matchingFileNameFallback = null;
            PostFileItem fallback = null;
            PostFileItem scmodFallback = null;
            foreach (PostFileItem file in version.Files)
            {
                if (file == null || string.IsNullOrWhiteSpace(file.Url) || !CommunityLocalModMatchingRules.IsModFile(version, file))
                {
                    continue;
                }

                bool isScmod = EndsWithScmod(file.Filename) || EndsWithScmod(file.Url);
                bool packageMatches = CommunityLocalModMatchingRules.MatchesText(file.PackageName, expectedPackageName);
                bool fileNameMatches = CommunityLocalModMatchingRules.MatchesFileName(file.Filename, preferredFileName);
                hasAnyPackageMetadata |= !string.IsNullOrWhiteSpace(file.PackageName);
                if (packageMatches && isScmod)
                {
                    return file;
                }
                if (matchingFileNameScmod == null && fileNameMatches && isScmod)
                {
                    matchingFileNameScmod = file;
                }
                if (matchingPackageFallback == null && packageMatches)
                {
                    matchingPackageFallback = file;
                }
                if (matchingFileNameFallback == null && fileNameMatches)
                {
                    matchingFileNameFallback = file;
                }
                if (scmodFallback == null && isScmod)
                {
                    scmodFallback = file;
                }
                fallback ??= file;
            }

            if (matchingFileNameScmod != null)
            {
                return matchingFileNameScmod;
            }
            if (matchingPackageFallback != null)
            {
                return matchingPackageFallback;
            }
            if (matchingFileNameFallback != null)
            {
                return matchingFileNameFallback;
            }
            if (requirePackageMatch && hasAnyPackageMetadata)
            {
                return null;
            }
            return scmodFallback ?? fallback;
        }

        private static bool IsModVersion(PostVersionItem version)
        {
            return version != null && (version.FileType == CommunityFileType.Unknown || CommunityFileTypes.IsModLike(version.FileType));
        }

        public static int CompareVersions(PostVersionItem left, PostVersionItem right)
        {
            int versionCompare = CompareNuGetVersions(left?.NuGetVersion, right?.NuGetVersion);
            if (versionCompare != 0)
            {
                return versionCompare;
            }

            return ParseVersionTime(left).CompareTo(ParseVersionTime(right));
        }

        private static int CompareNuGetVersions(NuGetVersion left, NuGetVersion right)
        {
            if (left != null && right != null)
            {
                return VersionComparer.VersionRelease.Compare(left, right);
            }
            return left != null ? 1 : right != null ? -1 : 0;
        }

        public static bool IsNewer(NuGetVersion candidate, NuGetVersion current)
        {
            return candidate != null
                && (current == null || VersionComparer.VersionRelease.Compare(candidate, current) > 0);
        }

        private static DateTime ParseVersionTime(PostVersionItem version)
        {
            string value = !string.IsNullOrWhiteSpace(version?.UpdatedAt) ? version.UpdatedAt : version?.CreatedAt;
            // 固定 InvariantCulture 防区域解析歧义；无时区串按 UTC 解读并统一转 UTC 比较。
            return DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTime time)
                ? time
                : DateTime.MinValue;
        }

        private static bool EndsWithScmod(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return Uri.UnescapeDataString(value.Trim()).EndsWith(".scmod", StringComparison.OrdinalIgnoreCase);
        }
    }
}
