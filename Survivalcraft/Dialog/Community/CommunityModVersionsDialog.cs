using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;
using Engine;
using Engine.Graphics;
using NuGet.Versioning;

namespace Game
{
    public sealed class CommunityModVersionsDialog : Dialog
    {
        private readonly List<CommunityModVersionWidget> m_versionWidgets = [];
        private readonly HashSet<string> m_expandedVersionKeys = [];
        private readonly IReadOnlyList<PostVersionItem> m_versions;
        private readonly IReadOnlyDictionary<int, string> m_gameVersionsById;
        private readonly string m_currentVersion;
        private readonly string m_currentApiVersion;
        private readonly string m_currentGameVersion;
        private readonly IReadOnlyList<VersionListItem> m_versionCatalog;
        private readonly StackPanelWidget m_listPanel;
        private readonly Action<PostVersionItem> m_showDetails;
        private readonly Action<PostVersionItem, PostFileItem> m_download;

        public CommunityModVersionsDialog(
            string postTitle,
            string currentVersion,
            string currentApiVersion,
            string currentGameVersion,
            IReadOnlyList<PostVersionItem> versions,
            IReadOnlyDictionary<int, string> gameVersionsById,
            IReadOnlyList<VersionListItem> versionCatalog,
            Action<PostVersionItem> showDetails,
            Action<PostVersionItem, PostFileItem> download)
        {
            m_versions = versions ?? [];
            m_gameVersionsById = gameVersionsById ?? new Dictionary<int, string>();
            m_currentVersion = ExternalVersionParser.Normalize(currentVersion);
            m_currentApiVersion = currentApiVersion ?? string.Empty;
            m_currentGameVersion = currentGameVersion ?? string.Empty;
            m_versionCatalog = versionCatalog ?? [];
            m_showDetails = showDetails;
            m_download = download;

            XElement node = ContentManager.Get<XElement>("Dialogs/ScbbsPlusModVersionsDialog");
            LoadContents(this, node);
            Children.Find<LabelWidget>("ScbbsPlusModVersionsDialog.PostTitleLabel").Text =
                string.IsNullOrWhiteSpace(postTitle) ? LanguageControl.Get("ScbbsPlusModVersionsDialog", "Text", "CommunityPost") : postTitle;
            m_listPanel = Children.Find<StackPanelWidget>("ScbbsPlusModVersionsDialog.ListPanel");
            BuildVersionList();
        }

        private void BuildVersionList()
        {
            m_listPanel.Children.Clear();
            m_versionWidgets.Clear();

            if (m_versions == null || m_versions.Count == 0)
            {
                m_listPanel.Children.Add(new LabelWidget
                {
                    Text = LanguageControl.Get("ScbbsPlusModVersionsDialog", "NoVersions"),
                    Color = new Color(200, 200, 200),
                    FontScale = 0.9f,
                    HorizontalAlignment = WidgetAlignment.Center
                });
                return;
            }

            foreach (PostVersionItem version in m_versions)
            {
                CommunityModVersionDisplayData data = BuildVersionDisplayData(version);
                CommunityModVersionWidget widget = new();
                widget.Bind(data, m_expandedVersionKeys.Contains(data.VersionKey));
                m_versionWidgets.Add(widget);
                m_listPanel.Children.Add(widget);
            }
        }

        public override void Update()
        {
            if (Input.Back
                || Input.Cancel
                || (Input.Tap.HasValue && !HitTest(Input.Tap.Value)))
            {
                DialogsManager.HideDialog(this);
                return;
            }

            foreach (CommunityModVersionWidget widget in m_versionWidgets)
            {
                if (widget.IsDetailsClicked)
                {
                    DialogsManager.HideDialog(this);
                    m_showDetails?.Invoke(widget.Version);
                    return;
                }
            }

            foreach (CommunityModVersionWidget widget in m_versionWidgets)
            {
                if (!widget.IsFilesToggleClicked)
                {
                    continue;
                }

                if (!m_expandedVersionKeys.Remove(widget.VersionKey))
                {
                    m_expandedVersionKeys.Add(widget.VersionKey);
                }
                BuildVersionList();
                return;
            }

            foreach (CommunityModVersionWidget widget in m_versionWidgets)
            {
                if (!widget.TryGetClickedFile(out PostFileItem file))
                {
                    continue;
                }

                DialogsManager.HideDialog(this);
                m_download?.Invoke(widget.Version, file);
                return;
            }
        }

        private CommunityModVersionDisplayData BuildVersionDisplayData(PostVersionItem version)
        {
            int fileCount = version?.Files?.Length ?? 0;
            bool hasExpandableFiles = fileCount > 1;
            bool hasSingleFile = fileCount == 1;
            bool isCurrent = CommunityVersionCompatibilityFormatter.IsCurrentVersion(version, m_currentVersion);
            VersionCompatibilityInfo compatibility = CommunityVersionCompatibilityFormatter.GetVersionCompatibilityInfo(
                version,
                m_currentApiVersion,
                m_currentGameVersion,
                m_gameVersionsById,
                m_versionCatalog);
            List<CommunityModFileDisplayData> files = [];
            foreach (PostFileItem file in version?.Files ?? [])
            {
                files.Add(new CommunityModFileDisplayData
                {
                    File = file,
                    FileTitle = FormatFileTitle(file),
                    DownloadsSizeText = string.Format(
                        CultureInfo.InvariantCulture,
                        LanguageControl.Get("ScbbsPlusModVersionsDialog", "DownloadsSize"),
                        file?.DownloadCount ?? 0,
                        FormatFileSize(file?.Size))
                });
            }

            return new CommunityModVersionDisplayData
            {
                Version = version,
                VersionKey = GetVersionKey(version),
                VersionTitle = isCurrent
                    ? $"{CommunityVersionDetailFormatter.FormatVersionTitle(version)}  {LanguageControl.Get("ScbbsPlusModVersionsDialog", "Current")}"
                    : CommunityVersionDetailFormatter.FormatVersionTitle(version),
                FileTitle = hasSingleFile ? FormatFileTitle(version.Files[0]) : string.Empty,
                CompatibilityText = string.Format(
                    CultureInfo.InvariantCulture,
                    LanguageControl.Get("ScbbsPlusModVersionsDialog", "GameVersionsPublished"),
                    $"{compatibility.StatusText}  ",
                    compatibility.VersionNamesText,
                    CommunityTimeFormatter.FormatTime(version?.CreatedAt)),
                TitleColor = isCurrent ? new Color(255, 230, 140) : Color.White,
                CompatibilityColor = isCurrent ? new Color(255, 230, 140) : compatibility.Color,
                IsCurrent = isCurrent,
                HasSingleFile = hasSingleFile,
                HasExpandableFiles = hasExpandableFiles,
                Files = files
            };
        }

        private static string GetVersionKey(PostVersionItem version)
        {
            if (version == null)
            {
                return "null";
            }
            if (version.Id > 0)
            {
                return $"id:{version.Id}";
            }
            return $"text:{version.Title}|{version.Version}|{version.CreatedAt}";
        }

        private static string FormatFileTitle(PostFileItem file)
        {
            return string.IsNullOrWhiteSpace(file?.Filename) ? LanguageControl.Get("ScbbsPlusModVersionsDialog", "Text", "UnnamedFile") : file.Filename.Trim();
        }

        private static string FormatFileSize(string value)
        {
            string text = CommunityVersionDetailFormatter.FormatEmpty(value);
            if (text == "-")
            {
                return text;
            }
            return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long size)
                ? DataSizeFormatter.Format(size)
                : text;
        }
    }
}
