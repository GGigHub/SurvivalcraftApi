using System.Collections.Generic;
using Engine;
using NuGet.Versioning;

namespace Game
{
    // 自模组 Presentation/Dialogs/ScbbsPlusModVersionsDialog.cs 原样抽出（成员零改动，
    // 嵌套 CompatibilityDisplayInfo（现 VersionCompatibilityInfo）提为命名空间级）：帖子树渲染
    // 需要静态兼容性显示方法，而 ModVersionsDialog 对话框本体留待 Local 轮移植，故拆出本独立类型。

    /// <summary>
    /// 版本兼容性显示信息（原 ModVersionsDialog 嵌套类型，提为命名空间级）。
    /// </summary>
    public sealed class VersionCompatibilityInfo
    {
        public string StatusText { get; init; }
        public string VersionNamesText { get; init; }
        public Color Color { get; init; }
    }

    internal static class CommunityVersionCompatibilityFormatter
    {
        public static VersionCompatibilityInfo GetVersionCompatibilityInfo(
            PostVersionItem version,
            string currentApiVersion,
            string currentGameVersion,
            IReadOnlyDictionary<int, string> gameVersionsById,
            IReadOnlyList<VersionListItem> versionCatalog)
        {
            ModCompatibilityResult compatibility = ModCompatibility.Evaluate(
                version,
                currentApiVersion,
                currentGameVersion,
                gameVersionsById,
                versionCatalog);

            return new VersionCompatibilityInfo
            {
                StatusText = compatibility.HasCompatibilityTargets
                    ? GetCompatibilityStatusText(compatibility.Level)
                    : LanguageControl.Get("ScbbsPlusModVersionsDialog", "NoCompatibilityVersionSpecified"),
                VersionNamesText = compatibility.HasCompatibilityTargets
                    ? string.Join("、", compatibility.VersionNames)
                    : "-",
                Color = GetCompatibilityColor(compatibility.Level)
            };
        }

        private static string GetCompatibilityStatusText(ModCompatibilityLevel level)
        {
            return level switch
            {
                ModCompatibilityLevel.Exact => LanguageControl.Get("ScbbsPlusModVersionsDialog", "FullyCompatible"),
                ModCompatibilityLevel.Risky => LanguageControl.Get("ScbbsPlusModVersionsDialog", "PotentialCompatibilityIssues"),
                ModCompatibilityLevel.Incompatible => LanguageControl.Get("ScbbsPlusModVersionsDialog", "Incompatible"),
                _ => LanguageControl.Get("ScbbsPlusModVersionsDialog", "NoCompatibilityVersionSpecified")
            };
        }

        private static Color GetCompatibilityColor(ModCompatibilityLevel level)
        {
            return level switch
            {
                ModCompatibilityLevel.Exact => new Color(120, 220, 140),
                ModCompatibilityLevel.Risky => new Color(255, 210, 120),
                ModCompatibilityLevel.Incompatible => new Color(255, 150, 150),
                _ => new Color(150, 150, 150)
            };
        }

        internal static bool IsCurrentVersion(PostVersionItem version, string normalizedCurrentVersion)
        {
            // normalizedCurrentVersion 的 null/空白/不可解析均由 ParseOrNull 返回 null 兜住。
            if (version == null)
            {
                return false;
            }

            // 统一判定语义：候选文本经 Normalize 剥前缀后能解析 → NuGetVersion 相等
            // （2.1.0 == 2.1）；解析不出（如 "2.1 beta"、"2.1 正式版" 等带尾注/前注的
            // 文本）→ 拒绝模糊命中，判不当前。作者选定语义：宁可漏高亮也不误命中，
            // 不做尾部截断（会把 "2.1 beta" 这类 prerelease 尾注一并放行）。
            NuGetVersion current = ExternalVersionParser.ParseOrNull(normalizedCurrentVersion);
            if (current == null)
            {
                return false;
            }

            if (version.Files != null)
            {
                foreach (PostFileItem file in version.Files)
                {
                    if (IsSameParsedVersion(file?.PackageVersion, current))
                    {
                        return true;
                    }
                }
            }

            if (IsSameParsedVersion(version.Title, current))
            {
                return true;
            }

            // version.Version 可能是后端列表序号（"1"、"2"），纯整数不作版本比对。
            string versionText = ExternalVersionParser.Normalize(version.Version);
            return !CommunityVersionDetailFormatter.IsIntegerText(versionText)
                && IsSameParsedVersion(versionText, current);
        }

        private static bool IsSameParsedVersion(string text, NuGetVersion current)
        {
            NuGetVersion parsed = ExternalVersionParser.ParseOrNull(text);
            return parsed != null && parsed == current;
        }
    }
}
