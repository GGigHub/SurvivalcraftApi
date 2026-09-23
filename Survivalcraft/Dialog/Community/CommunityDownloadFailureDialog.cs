using System;
using System.Xml.Linq;
using Engine;

namespace Game
{
    public sealed class CommunityDownloadFailureDialog : Dialog
    {
        private readonly ButtonWidget[] m_sourceButtons;
        private readonly ButtonWidget m_copyLinkButton;
        private readonly Action<CommunityDownloadSourceOption> m_useSource;
        private readonly string m_downloadUrl;

        public CommunityDownloadFailureDialog(
            string fileName,
            string downloadUrl,
            string error,
            Action<CommunityDownloadSourceOption> useSource)
        {
            m_useSource = useSource;
            m_downloadUrl = downloadUrl ?? string.Empty;

            XElement node = ContentManager.Get<XElement>("Dialogs/ScbbsPlusDownloadFailureDialog");
            LoadContents(this, node);
            string title = string.IsNullOrWhiteSpace(fileName)
                ? LanguageControl.Get("ScbbsPlusDownloadFailureDialog", "DownloadError")
                : $"{LanguageControl.Get("ScbbsPlusDownloadFailureDialog", "DownloadError")} - {fileName}";
            Children.Find<LabelWidget>("ScbbsPlusDownloadFailureDialog.Title").Text = title;
            Children.Find<LabelWidget>("ScbbsPlusDownloadFailureDialog.LinkLabel").Text =
                string.IsNullOrWhiteSpace(m_downloadUrl) ? "-" : m_downloadUrl;
            m_copyLinkButton = Children.Find<ButtonWidget>("ScbbsPlusDownloadFailureDialog.CopyLinkButton");
            m_copyLinkButton.IsEnabled = !string.IsNullOrWhiteSpace(m_downloadUrl);
            StackPanelWidget errorContent = Children.Find<StackPanelWidget>("ScbbsPlusDownloadFailureDialog.ErrorContent");
            errorContent.Children.Add(new LabelWidget
            {
                Text = error ?? LanguageControl.Get("ScbbsPlusDownloadFailureDialog", "Text", "UnknownError"),
                Margin = new Vector2(4f, 2f),
                Color = new Color(235, 235, 235),
                FontScale = 0.78f,
                WordWrap = true
            });
            CommunityDownloadSourceOption[] sourceOptions = Enum.GetValues<CommunityDownloadSourceOption>();
            m_sourceButtons = new ButtonWidget[sourceOptions.Length];
            for (int i = 0; i < sourceOptions.Length; i++)
            {
                m_sourceButtons[i] = Children.Find<ButtonWidget>($"ScbbsPlusDownloadFailureDialog.Source{i}Button");
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

            if (m_copyLinkButton.IsClicked && !string.IsNullOrWhiteSpace(m_downloadUrl))
            {
                ClipboardManager.ClipboardString = m_downloadUrl;
                return;
            }

            for (int i = 0; i < m_sourceButtons.Length; i++)
            {
                if (m_sourceButtons[i].IsClicked)
                {
                    DialogsManager.HideDialog(this);
                    m_useSource((CommunityDownloadSourceOption)i);
                    break;
                }
            }
        }
    }
}
