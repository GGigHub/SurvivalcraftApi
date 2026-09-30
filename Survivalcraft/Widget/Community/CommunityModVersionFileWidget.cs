using System.Xml.Linq;

namespace Game
{
    public sealed class CommunityModVersionFileWidget : FixedSizePanelWidget
    {
        private readonly LabelWidget m_fileTitleWidget;
        private readonly LabelWidget m_downloadsSizeWidget;
        private readonly ButtonWidget m_downloadButton;
        private PostFileItem m_file;

        public CommunityModVersionFileWidget()
        {
            XElement node = ContentManager.Get<XElement>("Widgets/CommunityModVersionFileWidget");
            LoadContents(this, node);
            m_fileTitleWidget = Children.Find<LabelWidget>("ScbbsPlusModVersionFileWidget.FileTitle");
            m_downloadsSizeWidget = Children.Find<LabelWidget>("ScbbsPlusModVersionFileWidget.DownloadsSize");
            m_downloadButton = Children.Find<ButtonWidget>("ScbbsPlusModVersionFileWidget.DownloadButton");
            m_downloadButton.Text = LanguageControl.Get("ExternalContentScreen", "2");
        }

        public PostFileItem File => m_file;

        public bool IsDownloadClicked => m_downloadButton.IsClicked;

        internal void Bind(CommunityModFileDisplayData data)
        {
            m_file = data?.File;
            m_fileTitleWidget.Text = data?.FileTitle ?? string.Empty;
            m_downloadsSizeWidget.Text = data?.DownloadsSizeText ?? string.Empty;
        }
    }
}
