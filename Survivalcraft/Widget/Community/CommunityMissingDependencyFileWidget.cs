using System.Xml.Linq;

namespace Game
{
    public sealed class CommunityMissingDependencyFileWidget : FixedSizePanelWidget
    {
        private readonly LabelWidget m_fileTitleWidget;
        private readonly LabelWidget m_metadataWidget;
        private readonly ButtonWidget m_downloadButton;

        public CommunityMissingDependencyFileWidget()
        {
            XElement node = ContentManager.Get<XElement>("Widgets/ScbbsPlusMissingDependencyFileWidget");
            LoadContents(this, node);
            m_fileTitleWidget = Children.Find<LabelWidget>("ScbbsPlusMissingDependencyFileWidget.FileTitle");
            m_metadataWidget = Children.Find<LabelWidget>("ScbbsPlusMissingDependencyFileWidget.Metadata");
            m_downloadButton = Children.Find<ButtonWidget>("ScbbsPlusMissingDependencyFileWidget.DownloadButton");
            m_downloadButton.Text = LanguageControl.Get("ExternalContentScreen", "2");
        }

        public PostFileItem File { get; private set; }

        public bool IsDownloadClicked => m_downloadButton.IsClicked;

        internal void Bind(CommunityMissingDependencyFileDisplayData data, bool canDownload = true)
        {
            File = data?.File;
            m_fileTitleWidget.Text = data?.FileTitle ?? string.Empty;
            m_metadataWidget.Text = data?.MetadataText ?? string.Empty;
            m_downloadButton.IsEnabled = canDownload && File != null;
        }
    }
}
