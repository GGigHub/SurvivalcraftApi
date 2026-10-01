using System.Xml.Linq;

namespace Game {
    public class CommunityLoginDialog : Dialog {
        readonly Action<string, string> m_submit;
        readonly Action m_closed;
        readonly TextBoxWidget m_accountBox;
        readonly TextBoxWidget m_passwordBox;
        readonly LabelWidget m_errorLabel;
        readonly BevelledButtonWidget m_loginButton;
        readonly BevelledButtonWidget m_registerButton;
        readonly BevelledButtonWidget m_cancelButton;
        bool m_isBusy;

        public CommunityLoginDialog(Action<string, string> submit, Action closed = null) {
            m_submit = submit;
            m_closed = closed;
            XElement node = ContentManager.Get<XElement>("Dialogs/CommunityLoginDialog");
            LoadContents(this, node);
            m_accountBox = Children.Find<TextBoxWidget>("LoginDialog.AccountBox");
            m_passwordBox = Children.Find<TextBoxWidget>("LoginDialog.PasswordBox");
            m_errorLabel = Children.Find<LabelWidget>("LoginDialog.ErrorLabel");
            m_loginButton = Children.Find<BevelledButtonWidget>("LoginDialog.LoginButton");
            m_registerButton = Children.Find<BevelledButtonWidget>("LoginDialog.RegisterButton");
            m_cancelButton = Children.Find<BevelledButtonWidget>("LoginDialog.CancelButton");
            m_cancelButton.Text = LanguageControl.Cancel;
            m_accountBox.HasFocus = true;
        }

        public void SetBusy(bool isBusy) {
            m_isBusy = isBusy;
            m_loginButton.IsEnabled = !isBusy;
            m_cancelButton.IsEnabled = !isBusy;
            m_loginButton.Text = isBusy ? LanguageControl.Get("LoginDialog", "LoggingIn") : LanguageControl.Get("LoginDialog", "LogIn");
        }

        public void SetError(string message) {
            m_errorLabel.Text = message ?? string.Empty;
        }

        public override void Update() {
            if (!m_isBusy
                && (Input.Back || Input.Cancel || (Input.Tap.HasValue && !HitTest(Input.Tap.Value)) || m_cancelButton.IsClicked)) {
                DialogsManager.HideDialog(this);
                m_closed?.Invoke();
            }
            else if (!m_isBusy
                && m_registerButton.IsClicked) {
                WebBrowserManager.LaunchBrowser(CommunityServerManager.CurrentChineseInfo.WebsiteUrl);
            }
            else if (!m_isBusy
                && (Input.Ok || m_loginButton.IsClicked)) {
                SetError(null);
                m_submit?.Invoke(m_accountBox.Text, m_passwordBox.Text);
            }
        }
    }
}