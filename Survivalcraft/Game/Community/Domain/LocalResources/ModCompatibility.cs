using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using NuGet.Versioning;

namespace Game {
    enum ModCompatibilityLevel {
        Unknown = 0,
        Incompatible = 1,
        Risky = 2,
        Exact = 3
    }

    sealed class ModCompatibilityResult {
        public ModCompatibilityLevel Level { get; init; }
        public IReadOnlyList<string> VersionNames { get; init; }
        public bool HasCompatibilityTargets => VersionNames?.Count > 0;
    }

    static class ModCompatibility {
        public static ModCompatibilityResult Evaluate(PostVersionItem version,
            string currentApiVersion,
            string currentGameVersion,
            IReadOnlyDictionary<int, string> gameVersionsById,
            IReadOnlyList<VersionListItem> versionCatalog) {
            List<VersionCompatibilityTarget> targets = ResolveTargets(version, gameVersionsById, versionCatalog);
            return new ModCompatibilityResult {
                Level = EvaluateBestLevel(targets, currentApiVersion, currentGameVersion),
                VersionNames = targets.Select(target => target.DisplayName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            };
        }

        public static bool IsCompatibleWithCurrentVersion(PostVersionItem version,
            string currentApiVersion,
            string currentGameVersion,
            IReadOnlyDictionary<int, string> gameVersionsById,
            IReadOnlyList<VersionListItem> versionCatalog) => Evaluate(
                    version,
                    currentApiVersion,
                    currentGameVersion,
                    gameVersionsById,
                    versionCatalog
                )
                .Level
            == ModCompatibilityLevel.Exact;

        public static IEnumerable<int> GetGameVersionIds(PostVersionItem version) {
            if (version == null) {
                yield break;
            }
            if (version.GameVersionIds != null) {
                foreach (int id in version.GameVersionIds) {
                    if (id > 0) {
                        yield return id;
                    }
                }
            }
            if (version.ExtensionData == null) {
                yield break;
            }
            string[] keys = ["gameVersionIds", "game_version_ids"];
            foreach (string key in keys) {
                if (version.ExtensionData.TryGetValue(key, out JsonElement element)) {
                    foreach (int id in ReadIntList(element)) {
                        if (id > 0) {
                            yield return id;
                        }
                    }
                }
            }
        }

        static List<VersionCompatibilityTarget> ResolveTargets(PostVersionItem version,
            IReadOnlyDictionary<int, string> gameVersionsById,
            IReadOnlyList<VersionListItem> versionCatalog) {
            List<VersionCompatibilityTarget> result = [];
            Dictionary<int, VersionListItem> catalogById = BuildVersionCatalogById(versionCatalog);
            foreach (int versionId in GetGameVersionIds(version)) {
                if (catalogById.TryGetValue(versionId, out VersionListItem item)) {
                    AddTarget(result, item.Type, GetVersionDisplayName(item), GetVersionCompareText(item), item.NuGetVersion);
                }
                else if (gameVersionsById != null
                    && gameVersionsById.TryGetValue(versionId, out string mapped)
                    && !string.IsNullOrWhiteSpace(mapped)) {
                    AddTarget(result, InferVersionType(mapped), mapped.Trim(), mapped.Trim());
                }
            }
            if (result.Count > 0) {
                return result;
            }
            string searchText = BuildVersionSearchText(version);
            if (string.IsNullOrWhiteSpace(searchText)
                || versionCatalog == null) {
                return result;
            }

            // 目录逐项匹配共享同一份归一化文本，循环外归一化一次。
            string normalizedSearch = NormalizeSearchText(searchText);
            if (string.IsNullOrWhiteSpace(normalizedSearch)) {
                return result;
            }
            foreach (VersionListItem item in versionCatalog) {
                if (item != null
                    && item.Status == 1
                    && VersionMentionedInText(normalizedSearch, item)) {
                    AddTarget(result, item.Type, GetVersionDisplayName(item), GetVersionCompareText(item), item.NuGetVersion);
                }
            }
            return result;
        }

        static void AddTarget(List<VersionCompatibilityTarget> targets,
            string type,
            string displayName,
            string compareText,
            NuGetVersion parsedVersion = null) {
            if (string.IsNullOrWhiteSpace(displayName)
                || string.IsNullOrWhiteSpace(compareText)
                || targets.Any(target => string.Equals(target.DisplayName, displayName, StringComparison.OrdinalIgnoreCase))) {
                return;
            }
            if (parsedVersion == null) {
                ExternalVersionParser.TryParse(compareText, out parsedVersion);
            }
            targets.Add(
                new VersionCompatibilityTarget {
                    Type = type ?? string.Empty, DisplayName = displayName.Trim(), CompareText = compareText.Trim(), NuGetVersion = parsedVersion
                }
            );
        }

        static ModCompatibilityLevel EvaluateBestLevel(IReadOnlyList<VersionCompatibilityTarget> targets,
            string currentApiVersion,
            string currentGameVersion) {
            ExternalVersionParser.TryParse(currentApiVersion, out NuGetVersion currentApiNuGetVersion);
            ExternalVersionParser.TryParse(currentGameVersion, out NuGetVersion currentGameNuGetVersion);
            ModCompatibilityLevel bestLevel = ModCompatibilityLevel.Unknown;
            if (targets == null) {
                return bestLevel;
            }
            foreach (VersionCompatibilityTarget target in targets) {
                ModCompatibilityLevel level = EvaluateLevel(
                    IsPluginType(target?.Type) || ContainsApiText(target?.CompareText) ? currentApiNuGetVersion : currentGameNuGetVersion,
                    target.NuGetVersion
                );
                if (level > bestLevel) {
                    bestLevel = level;
                }
            }
            return bestLevel;
        }

        static Dictionary<int, VersionListItem> BuildVersionCatalogById(IReadOnlyList<VersionListItem> versionCatalog) {
            Dictionary<int, VersionListItem> result = [];
            if (versionCatalog != null) {
                foreach (VersionListItem item in versionCatalog) {
                    if (item != null
                        && item.Id > 0) {
                        result[item.Id] = item;
                    }
                }
            }
            return result;
        }

        static string BuildVersionSearchText(PostVersionItem version) {
            StringBuilder builder = new();
            AppendSearchText(builder, version?.Title);
            AppendSearchText(builder, version?.Version);
            AppendSearchText(builder, version?.Content);
            if (version?.Files != null) {
                foreach (PostFileItem file in version.Files) {
                    AppendSearchText(builder, file?.Filename);
                    AppendSearchText(builder, file?.PackageVersion);
                    AppendSearchText(builder, file?.Remark);
                }
            }
            return builder.ToString();
        }

        static void AppendSearchText(StringBuilder builder, string text) {
            if (!string.IsNullOrWhiteSpace(text)) {
                builder.Append(' ');
                builder.Append(text);
            }
        }

        static bool VersionMentionedInText(string normalizedSearch, VersionListItem item) {
            return !string.IsNullOrWhiteSpace(normalizedSearch)
                && item != null
                && BuildVersionSearchAliases(item).Any(alias => AliasMentionedAtBoundary(normalizedSearch, alias));
        }

        // 版本目录别名集合有限且跨调用重复，按 pattern 缓存已编译正则，
        // 避免动态构造 pattern 击穿 Regex 静态缓存（上限 15 条）。
        static readonly ConcurrentDictionary<string, Regex> s_boundaryPatterns = new(StringComparer.Ordinal);

        static bool AliasMentionedAtBoundary(string normalizedSearch, string alias) {
            if (string.IsNullOrWhiteSpace(alias)) {
                return false;
            }
            // 边界匹配：别名前不得紧邻数字/点（排除 1.2.1 命中 2.1 这类版本号嵌套），
            // 别名以数字结尾时其后亦不得紧邻数字（排除 2.10 命中 2.1）。
            // 名字形态别名（字母结尾）后边界不设限：「name 2.1」归一化后是
            // name2.1，若后边界排除数字则名字+版本连写会全部漏判。
            string pattern = char.IsDigit(alias[^1]) ? $"(?<![0-9.]){Regex.Escape(alias)}(?![0-9])" : $"(?<![0-9.]){Regex.Escape(alias)}";
            return s_boundaryPatterns.GetOrAdd(pattern, p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                .IsMatch(normalizedSearch);
        }

        static IEnumerable<string> BuildVersionSearchAliases(VersionListItem item) {
            HashSet<string> aliases = new(StringComparer.OrdinalIgnoreCase);
            AddAlias(aliases, item?.Name);
            AddAlias(aliases, item?.Version);
            NuGetVersion version = item?.NuGetVersion;
            if (version != null) {
                if (IsPluginType(item?.Type)
                    || ContainsApiText(item?.Name)
                    || ContainsApiText(item?.Version)) {
                    AddAlias(aliases, $"api{version.Major}.{version.Minor}");
                }
                else {
                    AddAlias(aliases, $"{version.Major}.{version.Minor}");
                }
            }
            return aliases;
        }

        static void AddAlias(HashSet<string> aliases, string text) {
            string normalized = NormalizeSearchText(text);
            if (!string.IsNullOrWhiteSpace(normalized)) {
                aliases.Add(normalized);
            }
        }

        static string NormalizeSearchText(string text) {
            if (string.IsNullOrWhiteSpace(text)) {
                return string.Empty;
            }
            StringBuilder builder = new(text.Length);
            foreach (char ch in text) {
                if (char.IsLetterOrDigit(ch)
                    || ch == '.') {
                    builder.Append(char.ToLowerInvariant(ch));
                }
            }
            return builder.ToString();
        }

        static ModCompatibilityLevel EvaluateLevel(NuGetVersion currentVersion, NuGetVersion candidateVersion) {
            if (currentVersion == null
                || candidateVersion == null) {
                return ModCompatibilityLevel.Unknown;
            }
            if (currentVersion.Major == candidateVersion.Major
                && currentVersion.Minor == candidateVersion.Minor) {
                return ModCompatibilityLevel.Exact;
            }
            return currentVersion.Major == candidateVersion.Major && Math.Abs(currentVersion.Minor - candidateVersion.Minor) == 1
                ? ModCompatibilityLevel.Risky
                : ModCompatibilityLevel.Incompatible;
        }

        // DisplayName（展示与去重键）与 CompareText（比较基线选择 + 兜底解析）语义上是两个角色，
        // 但实现有意同源、都偏向 Name：
        // - 目录行可能只有 Name 带版本（如 "API 1.9" 而 Version 为空）——此时靠
        //   AddTarget 的 TryParse(compareText) 从 Name 兜底抠出版本号；
        // - 反向形态（Name 无数字、Version 是纯版本，如 "Name"/"2.1"）由
        //   VersionListItem.NuGetVersion（模型层从 Version 解析）覆盖，不依赖 CompareText；
        // - ContainsApiText(CompareText) 依赖 Name 形态（"API 1.9"）为漏标 Type 的行选 API 基线。
        // 拆成不同来源（如 CompareText 偏向 Version）会同时打破上述两条兜底，勿改。
        static string GetVersionDisplayName(VersionListItem item) =>
            !string.IsNullOrWhiteSpace(item?.Name) ? item.Name.Trim() : item?.Version?.Trim() ?? string.Empty;

        static string GetVersionCompareText(VersionListItem item) =>
            !string.IsNullOrWhiteSpace(item?.Name) ? item.Name.Trim() : item?.Version?.Trim() ?? string.Empty;

        static string InferVersionType(string versionText) => ContainsApiText(versionText) ? "plugin" : string.Empty;

        static bool IsPluginType(string type) => string.Equals(type, "plugin", StringComparison.OrdinalIgnoreCase);

        static bool ContainsApiText(string text) => !string.IsNullOrWhiteSpace(text) && text.Contains("api", StringComparison.OrdinalIgnoreCase);

        static bool TryReadInt(JsonElement element, out int value) {
            if (element.ValueKind == JsonValueKind.Number) {
                return element.TryGetInt32(out value);
            }
            if (element.ValueKind == JsonValueKind.String) {
                return int.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
            }
            value = 0;
            return false;
        }

        static IEnumerable<int> ReadIntList(JsonElement element) {
            if (element.ValueKind == JsonValueKind.Array) {
                foreach (JsonElement item in element.EnumerateArray()) {
                    if (TryReadInt(item, out int value)) {
                        yield return value;
                    }
                }
            }
            else if (TryReadInt(element, out int value)) {
                yield return value;
            }
        }

        sealed class VersionCompatibilityTarget {
            public string Type { get; init; }
            public string DisplayName { get; init; }
            public string CompareText { get; init; }
            public NuGetVersion NuGetVersion { get; init; }
        }
    }
}