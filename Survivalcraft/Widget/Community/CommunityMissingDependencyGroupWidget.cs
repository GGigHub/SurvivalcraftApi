using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Game
{
    public sealed class CommunityMissingDependencyGroupWidget : FixedSizePanelWidget
    {
        private readonly LabelWidget m_groupTextWidget;
        private readonly ButtonWidget m_toggleButton;
        private readonly StackPanelWidget m_filesPanel;
        private readonly List<CommunityMissingDependencyFileWidget> m_fileWidgets = [];
        private CommunityMissingDependencyGroupDisplayData m_displayData;
        private bool m_isExpanded;
        private bool m_canDownload = true;

        public CommunityMissingDependencyGroupWidget()
        {
            XElement node = ContentManager.Get<XElement>("Widgets/CommunityMissingDependencyGroupWidget");
            LoadContents(this, node);
            m_groupTextWidget = Children.Find<LabelWidget>("ScbbsPlusMissingDependencyGroupWidget.GroupText");
            m_toggleButton = Children.Find<ButtonWidget>("ScbbsPlusMissingDependencyGroupWidget.ToggleButton");
            m_filesPanel = Children.Find<StackPanelWidget>("ScbbsPlusMissingDependencyGroupWidget.FilesPanel");
        }

        public string GroupKey { get; private set; }

        internal void Bind(CommunityMissingDependencyGroupDisplayData data, bool isExpanded, bool canDownload = true)
        {
            m_displayData = data ?? throw new ArgumentNullException(nameof(data));
            GroupKey = data.GroupKey;
            m_canDownload = canDownload;
            SetExpanded(isExpanded);
        }

        public override void Update()
        {
            if (m_toggleButton.IsClicked)
            {
                SetExpanded(!m_isExpanded);
            }
        }

        internal void SetExpanded(bool isExpanded)
        {
            if (m_displayData == null)
            {
                return;
            }

            m_isExpanded = isExpanded;
            m_groupTextWidget.Text = isExpanded
                ? m_displayData.ExpandedGroupText ?? string.Empty
                : m_displayData.GroupText ?? string.Empty;
            m_toggleButton.Text = isExpanded
                ? LanguageControl.Get("MissingDependenciesDialog", "Collapse")
                : LanguageControl.Get("MissingDependenciesDialog", "Expand");
            m_filesPanel.IsVisible = isExpanded;
            m_filesPanel.Children.Clear();
            m_fileWidgets.Clear();
            if (isExpanded)
            {
                foreach (CommunityMissingDependencyFileDisplayData file in m_displayData.Files)
                {
                    CommunityMissingDependencyFileWidget widget = new();
                    widget.Bind(file, m_canDownload);
                    m_fileWidgets.Add(widget);
                    m_filesPanel.Children.Add(widget);
                }
            }
        }

        internal bool TryGetClickedFile(out PostFileItem file)
        {
            foreach (CommunityMissingDependencyFileWidget fileWidget in m_fileWidgets)
            {
                if (fileWidget.IsDownloadClicked && fileWidget.File != null)
                {
                    file = fileWidget.File;
                    return true;
                }
            }

            file = null;
            return false;
        }
    }
}
