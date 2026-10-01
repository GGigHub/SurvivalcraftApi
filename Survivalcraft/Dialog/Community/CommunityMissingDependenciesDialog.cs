using System.Globalization;
using System.Xml.Linq;
using NuGet.Versioning;

namespace Game {
    public sealed class CommunityMissingDependenciesDialog : Dialog {
        readonly ButtonWidget m_downloadAllButton;
        readonly ButtonWidget m_closeButton;
        readonly StackPanelWidget m_listPanel;
        readonly List<FileGroup> m_fileGroups;
        readonly List<string> m_missingDependencyLabels;
        readonly List<PostFileItem> m_primaryDownloadFiles;
        readonly List<CommunityMissingDependencyFileWidget> m_fileWidgets = [];
        readonly List<CommunityMissingDependencyGroupWidget> m_groupWidgets = [];
        readonly Action<PostFileItem> m_download;
        readonly Action<IReadOnlyList<PostFileItem>> m_downloadAll;
        readonly bool m_groupFilesByPackageName;

        public CommunityMissingDependenciesDialog(string modName,
            IReadOnlyList<PostFileItem> files,
            Action<PostFileItem> download,
            Action<IReadOnlyList<PostFileItem>> downloadAll = null,
            string title = null,
            bool groupFilesByPackageName = false,
            IReadOnlyList<string> missingDependencyLabels = null,
            IReadOnlyDictionary<string, string> preferredVersionsByPackage = null) {
            m_download = download;
            m_downloadAll = downloadAll;
            m_groupFilesByPackageName = groupFilesByPackageName;
            m_missingDependencyLabels = BuildMissingDependencyLabels(missingDependencyLabels);
            Dictionary<string, string> preferredVersions = BuildPreferredVersionsByPackage(preferredVersionsByPackage);
            XElement node = ContentManager.Get<XElement>("Dialogs/CommunityMissingDependenciesDialog");
            LoadContents(this, node);
            Children.Find<LabelWidget>("MissingDependenciesDialog.TitleLabel").Text = string.IsNullOrWhiteSpace(title)
                ? LanguageControl.Get("MissingDependenciesDialog", "DependenciesFound")
                : title;
            Children.Find<LabelWidget>("MissingDependenciesDialog.ModNameLabel").Text = string.IsNullOrWhiteSpace(modName)
                ? LanguageControl.Get("MissingDependenciesDialog", "LocalMod")
                : modName;
            m_listPanel = Children.Find<StackPanelWidget>("MissingDependenciesDialog.ListPanel");
            m_fileGroups = BuildFileGroups(files, groupFilesByPackageName, preferredVersions);
            m_primaryDownloadFiles = BuildPrimaryDownloadFiles(m_fileGroups);
            BuildFileRows();
            m_downloadAllButton = Children.Find<ButtonWidget>("MissingDependenciesDialog.DownloadAllButton");
            m_downloadAllButton.IsEnabled = m_primaryDownloadFiles.Count > 0 && m_downloadAll != null;
            m_closeButton = Children.Find<ButtonWidget>("MissingDependenciesDialog.CloseButton");
        }

        public override void Update() {
            if (m_closeButton.IsClicked
                || Input.Back
                || Input.Cancel
                || (Input.Tap.HasValue && !HitTest(Input.Tap.Value))) {
                DialogsManager.HideDialog(this);
                return;
            }
            if (m_downloadAllButton.IsClicked
                && m_downloadAllButton.IsEnabled
                && m_downloadAll != null) {
                DialogsManager.HideDialog(this);
                m_downloadAll(m_primaryDownloadFiles);
                return;
            }
            foreach (CommunityMissingDependencyFileWidget widget in m_fileWidgets) {
                if (widget.IsDownloadClicked
                    && widget.File != null
                    && m_download != null) {
                    DialogsManager.HideDialog(this);
                    m_download(widget.File);
                    return;
                }
            }
            foreach (CommunityMissingDependencyGroupWidget widget in m_groupWidgets) {
                if (widget.TryGetClickedFile(out PostFileItem file)
                    && m_download != null) {
                    DialogsManager.HideDialog(this);
                    m_download(file);
                    return;
                }
            }
        }

        void BuildFileRows() {
            m_listPanel.Children.Clear();
            m_fileWidgets.Clear();
            m_groupWidgets.Clear();
            foreach (FileGroup group in m_fileGroups) {
                CommunityMissingDependencyFileWidget fileWidget = new();
                fileWidget.Bind(BuildFileDisplayData(group.Files[0]), m_download != null);
                m_fileWidgets.Add(fileWidget);
                m_listPanel.Children.Add(fileWidget);
                if (group.Files.Count > 1) {
                    CommunityMissingDependencyGroupWidget groupWidget = new();
                    groupWidget.Bind(BuildGroupDisplayData(group), false, m_download != null);
                    m_groupWidgets.Add(groupWidget);
                    m_listPanel.Children.Add(groupWidget);
                }
            }
            AddMissingDependencyPackageRows();
        }

        void AddMissingDependencyPackageRows() {
            if (m_missingDependencyLabels.Count == 0) {
                return;
            }

            // The section header belongs to the list, not to any individual compact row.
            CommunityMissingDependencyPackageHeaderWidget headerWidget = new();
            headerWidget.Bind(LanguageControl.Get("MissingDependenciesDialog", "ResourcePackageNameNotFound"), m_fileGroups.Count > 0);
            m_listPanel.Children.Add(headerWidget);
            foreach (string label in m_missingDependencyLabels) {
                CommunityMissingDependencyPackageWidget packageWidget = new();
                packageWidget.Bind(BuildPackageDisplayData(label));
                m_listPanel.Children.Add(packageWidget);
            }
        }

        static CommunityMissingDependencyFileDisplayData BuildFileDisplayData(PostFileItem file) => new() {
            File = file,
            FileTitle = string.IsNullOrWhiteSpace(file?.Filename) ? LanguageControl.Get("MissingDependenciesDialog", "UnnamedFile") : file.Filename,
            MetadataText = string.Format(
                CultureInfo.InvariantCulture,
                LanguageControl.Get("MissingDependenciesDialog", "PublishTimePackageVersionFormat"),
                CommunityTimeFormatter.FormatTime(!string.IsNullOrWhiteSpace(file?.CreatedAt) ? file.CreatedAt : file?.UpdatedAt),
                CommunityVersionDetailFormatter.FormatEmpty(file?.PackageName),
                CommunityVersionDetailFormatter.FormatEmpty(file?.PackageVersion)
            )
        };

        internal static string GetCollapsedGroupMessageToken(bool groupFilesByPackageName, string preferredVersion) => groupFilesByPackageName
            ? !string.IsNullOrWhiteSpace(preferredVersion) ? "CollapsedPackageGroupLatestInRange" : "CollapsedPackageGroupLatestOnly"
            : "CollapsedFileGroupLatestOnly";

        CommunityMissingDependencyGroupDisplayData BuildGroupDisplayData(FileGroup group) {
            int hiddenCount = Math.Max(0, group.Files.Count - 1);
            string collapsedToken = GetCollapsedGroupMessageToken(m_groupFilesByPackageName, group.PreferredVersion);
            return new CommunityMissingDependencyGroupDisplayData {
                GroupKey = group.Key,
                GroupText =
                    string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("MissingDependenciesDialog", collapsedToken), hiddenCount),
                ExpandedGroupText = string.Format(
                    CultureInfo.InvariantCulture,
                    m_groupFilesByPackageName
                        ? LanguageControl.Get("MissingDependenciesDialog", "MoreModsWithTheSamePackageName")
                        : LanguageControl.Get("MissingDependenciesDialog", "MoreDuplicateFiles"),
                    hiddenCount
                ),
                Files = [.. group.Files.Skip(1).Select(BuildFileDisplayData)]
            };
        }

        static CommunityMissingDependencyPackageDisplayData BuildPackageDisplayData(string label) => new() {
            Text = CommunityVersionDetailFormatter.FormatEmpty(label),
            DetailText = LanguageControl.Get("MissingDependenciesDialog", "NoDownloadableFile")
        };

        static List<FileGroup> BuildFileGroups(IReadOnlyList<PostFileItem> files,
            bool groupFilesByPackageName,
            IReadOnlyDictionary<string, string> preferredVersionsByPackage) {
            List<FileGroup> groups = [];
            Dictionary<string, FileGroup> groupsByKey = new(StringComparer.Ordinal);
            foreach (PostFileItem file in files ?? []) {
                if (file == null) {
                    continue;
                }
                string key = MakeFileGroupKey(file, groupFilesByPackageName);
                if (!groupsByKey.TryGetValue(key, out FileGroup group)) {
                    group = new FileGroup(key, GetPreferredVersion(file, groupFilesByPackageName, preferredVersionsByPackage));
                    groupsByKey[key] = group;
                    groups.Add(group);
                }
                group.Files.Add(file);
            }
            foreach (FileGroup group in groups) {
                group.Files.Sort((left, right) => CompareFilesWithinGroup(left, right, group.PreferredVersion));
            }
            groups.Sort(CompareGroupsByTimeDescending);
            return groups;
        }

        static string GetPreferredVersion(PostFileItem file,
            bool groupFilesByPackageName,
            IReadOnlyDictionary<string, string> preferredVersionsByPackage) {
            if (!groupFilesByPackageName
                || preferredVersionsByPackage == null) {
                return null;
            }
            string packageName = file?.PackageName?.Trim();
            if (string.IsNullOrWhiteSpace(packageName)) {
                return null;
            }
            return preferredVersionsByPackage.TryGetValue(packageName, out string preferredVersion) ? preferredVersion?.Trim() : null;
        }

        static string MakeFileGroupKey(PostFileItem file, bool groupFilesByPackageName) {
            string packageName = NormalizeKeyPart(file?.PackageName);
            if (groupFilesByPackageName && !string.IsNullOrEmpty(packageName)) {
                return $"pkg:{packageName}";
            }
            string filename = NormalizeKeyPart(file?.Filename);
            if (!string.IsNullOrEmpty(filename)) {
                return $"file:{filename}";
            }
            string packageVersion = NormalizeKeyPart(file?.PackageVersion);
            if (!string.IsNullOrEmpty(packageName)
                && !string.IsNullOrEmpty(packageVersion)) {
                return $"pkg:{packageName}\n{packageVersion}";
            }
            return $"id:{file?.Id ?? 0}:{NormalizeKeyPart(file?.Url)}";
        }

        static int CompareGroupsByTimeDescending(FileGroup left, FileGroup right) => CompareFilesByTimeDescending(
            left?.Files.Count > 0 ? left.Files[0] : null,
            right?.Files.Count > 0 ? right.Files[0] : null
        );

        static int CompareFilesWithinGroup(PostFileItem left, PostFileItem right, string preferredVersion) {
            int preferredCompare = CompareFilesByPreferredVersion(left, right, preferredVersion);
            return preferredCompare != 0
                ? preferredCompare
                : CompareFilesByTimeDescending(left, right);
        }

        internal static int CompareFilesByPreferredVersion(PostFileItem left, PostFileItem right, string preferredVersion) {
            if (string.IsNullOrWhiteSpace(preferredVersion)) {
                return 0;
            }
            VersionRange preferredRange = ModsManager.TryParseVersionRange(preferredVersion, out VersionRange parsedRange) ? parsedRange : null;
            ExternalVersionParser.TryParse(preferredVersion, out NuGetVersion preferredNuGetVersion);
            DependencyVersionMatchScore leftScore = GetDependencyVersionMatchScore(left, preferredRange, preferredNuGetVersion);
            DependencyVersionMatchScore rightScore = GetDependencyVersionMatchScore(right, preferredRange, preferredNuGetVersion);
            int compare = rightScore.CompareTo(leftScore);
            return compare != 0
                ? compare
                : CompareNuGetVersions(right?.NuGetVersion, left?.NuGetVersion);
        }

        static int CompareFilesByTimeDescending(PostFileItem left, PostFileItem right) {
            int timeCompare = GetFileTime(right).CompareTo(GetFileTime(left));
            if (timeCompare != 0) {
                return timeCompare;
            }
            return string.Compare(
                CommunityVersionDetailFormatter.FormatEmpty(left?.Filename),
                CommunityVersionDetailFormatter.FormatEmpty(right?.Filename),
                StringComparison.OrdinalIgnoreCase
            );
        }

        static DateTime GetFileTime(PostFileItem file) {
            string value = !string.IsNullOrWhiteSpace(file?.CreatedAt) ? file.CreatedAt : file?.UpdatedAt;
            return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime dateTime)
                ? dateTime.ToUniversalTime()
                : DateTime.MinValue;
        }

        static string NormalizeKeyPart(string value) => value?.Trim().ToLowerInvariant() ?? string.Empty;

        static Dictionary<string, string> BuildPreferredVersionsByPackage(IReadOnlyDictionary<string, string> preferredVersionsByPackage) {
            Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);
            if (preferredVersionsByPackage == null) {
                return result;
            }
            foreach (KeyValuePair<string, string> pair in preferredVersionsByPackage) {
                string packageName = pair.Key?.Trim();
                if (string.IsNullOrWhiteSpace(packageName)
                    || result.ContainsKey(packageName)) {
                    continue;
                }
                result[packageName] = pair.Value?.Trim();
            }
            return result;
        }

        static List<string> BuildMissingDependencyLabels(IReadOnlyList<string> labels) {
            List<string> result = [];
            HashSet<string> addedLabels = new(StringComparer.OrdinalIgnoreCase);
            foreach (string label in labels ?? []) {
                string normalized = label?.Trim();
                if (!string.IsNullOrWhiteSpace(normalized)
                    && addedLabels.Add(normalized)) {
                    result.Add(normalized);
                }
            }
            return result;
        }

        static List<PostFileItem> BuildPrimaryDownloadFiles(IReadOnlyList<FileGroup> groups) {
            List<PostFileItem> result = [];
            foreach (FileGroup group in groups ?? []) {
                PostFileItem file = group?.Files.Count > 0 ? group.Files[0] : null;
                if (file != null) {
                    result.Add(file);
                }
            }
            return result;
        }

        static DependencyVersionMatchScore GetDependencyVersionMatchScore(PostFileItem candidate, VersionRange range, NuGetVersion preferredVersion) {
            if (candidate?.NuGetVersion == null) {
                return default;
            }
            if (range != null) {
                bool isSatisfied = range.Satisfies(candidate.NuGetVersion);
                return new DependencyVersionMatchScore {
                    HasVersion = true, IsSatisfied = isSatisfied, IsExactMatch = isSatisfied && IsExactRange(range)
                };
            }
            return new DependencyVersionMatchScore {
                HasVersion = true,
                IsSatisfied = preferredVersion != null && VersionComparer.VersionRelease.Equals(candidate.NuGetVersion, preferredVersion),
                IsExactMatch = preferredVersion != null && VersionComparer.VersionRelease.Equals(candidate.NuGetVersion, preferredVersion)
            };
        }

        // 精确范围（[1.2.3] 形态）在候选排序中加权：命中且为单版本钉死时优先展示。
        static bool IsExactRange(VersionRange range) => range.MinVersion != null
            && range.MaxVersion != null
            && range.IsMinInclusive
            && range.IsMaxInclusive
            && VersionComparer.VersionRelease.Equals(range.MinVersion, range.MaxVersion);

        static int CompareNuGetVersions(NuGetVersion left, NuGetVersion right) {
            if (left != null
                && right != null) {
                return VersionComparer.VersionRelease.Compare(left, right);
            }
            return left != null ? 1 :
                right != null ? -1 : 0;
        }

        struct DependencyVersionMatchScore : IComparable<DependencyVersionMatchScore> {
            public bool HasVersion;
            public bool IsSatisfied;
            public bool IsExactMatch;

            public readonly int CompareTo(DependencyVersionMatchScore other) {
                int compare = HasVersion.CompareTo(other.HasVersion);
                if (compare != 0) {
                    return compare;
                }
                int satisfiedCompare = IsSatisfied.CompareTo(other.IsSatisfied);
                return satisfiedCompare != 0
                    ? satisfiedCompare
                    : IsExactMatch.CompareTo(other.IsExactMatch);
            }
        }

        sealed class FileGroup {
            public FileGroup(string key, string preferredVersion) {
                Key = key;
                PreferredVersion = preferredVersion;
            }

            public string Key { get; }
            public string PreferredVersion { get; }
            public List<PostFileItem> Files { get; } = [];
        }
    }
}