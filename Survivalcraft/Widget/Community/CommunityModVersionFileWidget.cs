using System.Xml.Linq;

namespace Game {
    public sealed class CommunityModVersionFileWidget : FixedSizePanelWidget {
        readonly LabelWidget m_fileTitleWidget;
        readonly LabelWidget m_downloadsSizeWidget;
        readonly ButtonWidget m_downloadButton;
        PostFileItem m_file;

        public CommunityModVersionFileWidget() {
            XElement node = ContentManager.Get<XElement>("Widgets/CommunityModVersionFileWidget");
            LoadContents(this, node);
            m_fileTitleWidget = Children.Find<LabelWidget>("ModVersionFileWidget.FileTitle");
            m_downloadsSizeWidget = Children.Find<LabelWidget>("ModVersionFileWidget.DownloadsSize");
            m_downloadButton = Children.Find<ButtonWidget>("ModVersionFileWidget.DownloadButton");
            m_downloadButton.Text = LanguageControl.Get("ExternalContentScreen", "2");
        }

        public PostFileItem File => m_file;

        public bool IsDownloadClicked => m_downloadButton.IsClicked;

        internal void Bind(CommunityModFileDisplayData data) {
            m_file = data?.File;
            m_fileTitleWidget.Text = data?.FileTitle ?? string.Empty;
            m_downloadsSizeWidget.Text = data?.DownloadsSizeText ?? string.Empty;
        }
    }
}