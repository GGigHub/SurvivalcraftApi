using System;
using NuGet.Versioning;

namespace Game
{
    internal static class CommunityModVersionSelector
    {
        public static VersionListItem FindBestMatch(VersionListItem[] versions, string apiVersion)
        {
            if (versions == null)
            {
                return null;
            }
            if (!ExternalVersionParser.TryParse(apiVersion, out NuGetVersion currentVersion))
            {
                return null;
            }

            VersionListItem bestMatch = null;
            VersionMatchScore bestScore = default;
            foreach (VersionListItem version in versions)
            {
                if (version == null
                    || version.Status != 1
                    || !string.Equals(version.Type, "plugin", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                VersionMatchScore score = CalculateScore(currentVersion, version.NuGetVersion, version.IsVersionLine);
                if (!score.HasVersion)
                {
                    continue;
                }

                if (bestMatch == null || score.CompareTo(bestScore) > 0)
                {
                    bestMatch = version;
                    bestScore = score;
                }
            }
            return bestMatch;
        }

        private static VersionMatchScore CalculateScore(
            NuGetVersion currentVersion,
            NuGetVersion candidateVersion,
            bool isVersionLine)
        {
            if (candidateVersion == null)
            {
                return default;
            }

            bool sameMajorMinor = currentVersion.Major == candidateVersion.Major
                && currentVersion.Minor == candidateVersion.Minor;

            return new VersionMatchScore
            {
                HasVersion = true,
                IsExact = VersionComparer.VersionRelease.Equals(currentVersion, candidateVersion),
                IsCompatibilityLine = sameMajorMinor && isVersionLine,
                SameMajorMinor = sameMajorMinor,
                PatchDistance = Math.Abs(currentVersion.Patch - candidateVersion.Patch)
            };
        }

        private struct VersionMatchScore : IComparable<VersionMatchScore>
        {
            public bool HasVersion;
            public bool IsExact;
            public bool IsCompatibilityLine;
            public bool SameMajorMinor;
            public int PatchDistance;

            public readonly int CompareTo(VersionMatchScore other)
            {
                int compare = HasVersion.CompareTo(other.HasVersion);
                if (compare != 0)
                {
                    return compare;
                }

                compare = IsExact.CompareTo(other.IsExact);
                if (compare != 0)
                {
                    return compare;
                }

                compare = IsCompatibilityLine.CompareTo(other.IsCompatibilityLine);
                if (compare != 0)
                {
                    return compare;
                }

                compare = SameMajorMinor.CompareTo(other.SameMajorMinor);
                if (compare != 0)
                {
                    return compare;
                }

                return other.PatchDistance.CompareTo(PatchDistance);
            }
        }
    }
}
