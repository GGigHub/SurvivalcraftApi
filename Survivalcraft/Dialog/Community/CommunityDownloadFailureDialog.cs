using System;
using System.Xml.Linq;
using Engine;

namespace Game
{
    public sealed class CommunityDownloadFailureDialog : Dialog
    {
        private readonly ButtonWidget m_switchSourceButton;
        private readonly ButtonWidget m_copyLinkButton;
        private readonly ButtonWidget m_closeButton;
        private readonly Action<CommunityDownloadSourceManager.Info> m_useSource;
        private readonly string m_downloadUrl;

        public CommunityDownloadFailureDialog(
            string fileName,
            string downloadUrl,
            string error,
            Action<CommunityDownloadSourceManager.Info> useSource)
        {
            m_useSource = useSource;
            m_downloadUrl = downloadUrl ?? string.Empty;

            XElement node = ContentManager.Get<XElement>("Dialogs/CommunityDownloadFailureDialog");
            LoadContents(this, node);
            string title = string.IsNullOrWhiteSpace(fileName)
                ? LanguageControl.Get("DownloadFailureDialog", "DownloadError")
                : $"{LanguageControl.Get("DownloadFailureDialog", "DownloadError")} - {fileName}";
            Children.Find<LabelWidget>("DownloadFailureDialog.Title").Text = title;
            Children.Find<LabelWidget>("DownloadFailureDialog.LinkLabel").Text =
                string.IsNullOrWhiteSpace(m_downloadUrl) ? "-" : m_downloadUrl;
            m_copyLinkButton = Children.Find<ButtonWidget>("DownloadFailureDialog.CopyLinkButton");
            m_copyLinkButton.IsEnabled = !string.IsNullOrWhiteSpace(m_downloadUrl);
            StackPanelWidget errorContent = Children.Find<StackPanelWidget>("DownloadFailureDialog.ErrorContent");
            errorContent.Children.Add(new LabelWidget
            {
                Text = error ?? LanguageControl.Get("DownloadFailureDialog", "UnknownError"),
                Color = new Color(255, 255, 255),
                FontScale = 0.8f,
                WordWrap = true
            });
            m_switchSourceButton = Children.Find<ButtonWidget>("DownloadFailureDialog.SwitchSourceButton");
            m_closeButton = Children.Find<ButtonWidget>("DownloadFailureDialog.CloseButton");
        }

        public override void Update()
        {
            if (m_closeButton.IsClicked
                || Input.Back
                || Input.Cancel
                || (Input.Tap.HasValue && !HitTest(Input.Tap.Value)))
            {
                DialogsManager.HideDialog(this);
                return;
            }

            if (m_copyLinkButton.IsClicked && !string.IsNullOrWhiteSpace(m_downloadUrl))
            {
                ClipboardManager.ClipboardString = m_downloadUrl;
                return;
            }

            if (m_switchSourceButton.IsClicked)
            {
                // 换源选择复用 DFM 的 ListSelectionDialog（标题/条目文案同款）；
                // 本框保持打开，用户取消选择仍可回到失败详情。
                DownloadFlowManager.Instance.ShowDownloadSourceSelectionDialog(option =>
                {
                    DialogsManager.HideDialog(this);
                    m_useSource(option);
                });
            }
        }
    }
}
