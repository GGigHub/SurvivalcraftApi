using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;
using Engine;

namespace Game
{
    public sealed class CommunityModVersionWidget : FixedSizePanelWidget
    {
        private readonly RectangleWidget m_backgroundWidget;
        private readonly LabelWidget m_versionTitleWidget;
        private readonly LabelWidget m_fileTitleWidget;
        private readonly LabelWidget m_compatibilityWidget;
        private readonly ButtonWidget m_detailsButton;
        private readonly ButtonWidget m_downloadButton;
        private readonly ButtonWidget m_filesButton;
        private readonly StackPanelWidget m_filesPanel;
        private readonly List<CommunityModVersionFileWidget> m_fileWidgets = [];
        private CommunityModVersionDisplayData m_displayData;

        public CommunityModVersionWidget()
        {
            XElement node = ContentManager.Get<XElement>("Widgets/ScbbsPlusModVersionWidget");
            LoadContents(this, node);
            m_backgroundWidget = Children.Find<RectangleWidget>("ScbbsPlusModVersionWidget.Background");
            m_versionTitleWidget = Children.Find<LabelWidget>("ScbbsPlusModVersionWidget.VersionTitle");
            m_fileTitleWidget = Children.Find<LabelWidget>("ScbbsPlusModVersionWidget.FileTitle");
            m_compatibilityWidget = Children.Find<LabelWidget>("ScbbsPlusModVersionWidget.Compatibility");
            m_detailsButton = Children.Find<ButtonWidget>("ScbbsPlusModVersionWidget.DetailsButton");
            m_downloadButton = Children.Find<ButtonWidget>("ScbbsPlusModVersionWidget.DownloadButton");
            m_filesButton = Children.Find<ButtonWidget>("ScbbsPlusModVersionWidget.FilesButton");
            m_filesPanel = Children.Find<StackPanelWidget>("ScbbsPlusModVersionWidget.FilesPanel");
            m_detailsButton.Text = LanguageControl.Get("ScbbsPlusModVersionWidget", "Details");
            m_downloadButton.Text = LanguageControl.Get("ExternalContentScreen", "2");
        }

        public string VersionKey => m_displayData?.VersionKey;

        public PostVersionItem Version => m_displayData?.Version;

        public bool IsDetailsClicked => m_detailsButton.IsClicked;

        public bool IsFilesToggleClicked => m_filesButton.IsClicked;

        internal void Bind(CommunityModVersionDisplayData data, bool isExpanded)
        {
            m_displayData = data ?? throw new ArgumentNullException(nameof(data));
            m_versionTitleWidget.Text = data.VersionTitle ?? string.Empty;
            m_versionTitleWidget.Color = data.TitleColor;
            m_fileTitleWidget.Text = data.FileTitle ?? string.Empty;
            m_fileTitleWidget.IsVisible = data.HasSingleFile;
            m_compatibilityWidget.Text = data.CompatibilityText ?? string.Empty;
            m_compatibilityWidget.Color = data.CompatibilityColor;

            m_downloadButton.IsVisible = data.HasSingleFile;
            m_filesButton.IsVisible = data.HasExpandableFiles;
            m_filesButton.Text = isExpanded
                ? LanguageControl.Get("ScbbsPlusModVersionWidget", "Collapse")
                : string.Format(
                    CultureInfo.InvariantCulture,
                    LanguageControl.Get("ScbbsPlusModVersionWidget", "Files"),
                    data.Files.Count);
            SetExpanded(isExpanded);
        }

        public void SetExpanded(bool isExpanded)
        {
            bool shouldShowFiles = m_displayData?.HasExpandableFiles == true && isExpanded;
            m_filesPanel.IsVisible = shouldShowFiles;
            m_filesPanel.Children.Clear();
            m_fileWidgets.Clear();

            if (shouldShowFiles)
            {
                foreach (CommunityModFileDisplayData fileData in m_displayData.Files)
                {
                    CommunityModVersionFileWidget fileWidget = new();
                    fileWidget.Bind(fileData);
                    m_fileWidgets.Add(fileWidget);
                    m_filesPanel.Children.Add(fileWidget);
                }
            }

            m_backgroundWidget.OutlineColor = m_displayData?.IsCurrent == true
                ? new Color(255, 230, 140, 230)
                : new Color(90, 90, 90, 190);
        }

        internal bool TryGetClickedFile(out PostFileItem file)
        {
            if (m_downloadButton.IsClicked && m_displayData?.HasSingleFile == true && m_displayData.Files.Count > 0)
            {
                file = m_displayData.Files[0].File;
                return file != null;
            }

            foreach (CommunityModVersionFileWidget fileWidget in m_fileWidgets)
            {
                if (fileWidget.IsDownloadClicked)
                {
                    file = fileWidget.File;
                    return file != null;
                }
            }

            file = null;
            return false;
        }
    }
}
