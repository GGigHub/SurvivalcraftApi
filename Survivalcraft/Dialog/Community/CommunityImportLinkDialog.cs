using System;
using System.Xml.Linq;

namespace Game
{
    public sealed class CommunityImportLinkDialog : Dialog
    {
        private readonly Action<string> m_submit;
        private readonly TextBoxWidget m_urlBox;
        private readonly BevelledButtonWidget m_confirmButton;
        private readonly BevelledButtonWidget m_cancelButton;

        public CommunityImportLinkDialog(string initialUrl, Action<string> submit)
        {
            m_submit = submit;

            XElement node = ContentManager.Get<XElement>("Dialogs/ScbbsPlusImportLinkDialog");
            LoadContents(this, node);
            m_urlBox = Children.Find<TextBoxWidget>("ScbbsPlusImportLinkDialog.UrlBox");
            m_confirmButton = Children.Find<BevelledButtonWidget>("ScbbsPlusImportLinkDialog.ConfirmButton");
            m_cancelButton = Children.Find<BevelledButtonWidget>("ScbbsPlusImportLinkDialog.CancelButton");
            m_cancelButton.Text = LanguageControl.Cancel;
            m_urlBox.Text = initialUrl ?? string.Empty;
            m_urlBox.HasFocus = true;
        }

        public override void Update()
        {
            if (Input.Back
                || Input.Cancel
                || (Input.Tap.HasValue && !HitTest(Input.Tap.Value))
                || m_cancelButton.IsClicked)
            {
                DialogsManager.HideDialog(this);
                return;
            }

            if (Input.Ok || m_confirmButton.IsClicked)
            {
                string value = m_urlBox.Text?.Trim() ?? string.Empty;
                DialogsManager.HideDialog(this);
                m_submit?.Invoke(value);
            }
        }
    }
}
