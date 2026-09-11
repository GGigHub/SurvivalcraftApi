using System;
using System.Globalization;
using System.Xml.Linq;
using Engine;

namespace Game
{
    internal sealed class MoreScreen : CommunityScreenBase
    {
        private static MoreScreen s_current;
        private static string s_sharedStatus = string.Empty;

        private LabelWidget m_accountInfo;
        private ButtonWidget m_authButton;
        private ButtonWidget m_importButton;
        private ButtonWidget m_downloadQueueButton;
        private ButtonWidget m_aboutModButton;
        private ButtonWidget m_topBarBackButton;
        private LabelWidget m_statusLabel;

        public MoreScreen()
        {
            XElement node = ContentManager.Get<XElement>("Screens/ScbbsPlusMoreScreen");
            LoadContents(this, node);
            m_accountInfo = Children.Find<LabelWidget>("ScbbsPlusMoreScreen.AccountInfo");
            m_authButton = Children.Find<ButtonWidget>("ScbbsPlusMoreScreen.AuthButton");
            m_importButton = Children.Find<ButtonWidget>("ScbbsPlusMoreScreen.ImportButton");
            m_downloadQueueButton = Children.Find<ButtonWidget>("ScbbsPlusMoreScreen.DownloadQueueButton");
            m_aboutModButton = Children.Find<ButtonWidget>("ScbbsPlusMoreScreen.AboutModButton");
            m_topBarBackButton = Children.Find<ButtonWidget>("TopBar.Back");
            m_statusLabel = Children.Find<LabelWidget>("ScbbsPlusMoreScreen.Status");
        }

        public override void Enter(object[] parameters)
        {
            CommunitySettingsManager.Instance.EnsureLoaded();
            s_current = this;
            RefreshControls();
            SetStatus(string.IsNullOrWhiteSpace(s_sharedStatus)
                ? LanguageControl.Get("ScbbsPlusMoreScreen", "StatusReady")
                : s_sharedStatus);
            AcquireRemoteImportHost();
            InstallDownloadFlowCallbacks(
                SetSharedStatus,
                () => CommunitySettingsManager.Instance.DownloadSourceSelectionMode,
                ResolveEffectiveDownloadSource);
        }

        public override void Leave()
        {
            ReleaseDownloadFlowCallbacks(releaseRemoteImportHost: true);
            DownloadFlowManager controller = DownloadFlowManager.Instance;
            if (controller.StatusReporter == (Action<string>)SetSharedStatus)
            {
                controller.StatusReporter = null;
            }
            if (s_current == this)
            {
                s_current = null;
            }
        }

        public override void Update()
        {
            if (Input.Back || Input.Cancel || m_topBarBackButton.IsClicked)
            {
                ScreensManager.GoBack();
                return;
            }

            if (m_authButton.IsClicked) AuthSessionManager.Instance.OpenAuthEntry();
            if (m_importButton.IsClicked) RemoteImportManager.OpenImportLinkDialog();
            if (m_downloadQueueButton.IsClicked) DownloadFlowManager.Instance.ShowQueueDialog();
            if (m_aboutModButton.IsClicked) OpenAboutMod();
        }

        internal static void SetSharedStatus(string status)
        {
            s_sharedStatus = status ?? string.Empty;
            s_current?.SetStatus(s_sharedStatus);
        }

        private void SetStatus(string status)
        {
            s_sharedStatus = status ?? string.Empty;
            m_statusLabel.Text = s_sharedStatus;
        }

        private void RefreshControls()
        {
            m_accountInfo.Text = GetAccountInfoText();
            m_authButton.Text = AuthSessionManager.Instance.IsLoggedIn
                ? LanguageControl.Get("ScbbsPlusMoreScreen", "Text", "LogOut")
                : LanguageControl.Get("ScbbsPlusMoreScreen", "Text", "LogIn");
        }

        private string GetAccountInfoText()
        {
            AuthSessionManager auth = AuthSessionManager.Instance;
            if (!auth.IsLoggedIn || auth.CurrentUser == null)
            {
                return LanguageControl.Get("ScbbsPlusCommunityScreen", "Main", "NotLoggedIn");
            }
            string account = string.IsNullOrWhiteSpace(auth.CurrentUser.Account)
                ? LanguageControl.Get("ScbbsPlusCommunityScreen", "Upload", "Update", "UnknownAccount")
                : auth.CurrentUser.Account.Trim();
            string nickname = string.IsNullOrWhiteSpace(auth.CurrentUser.Nickname) ? account : auth.CurrentUser.Nickname.Trim();
            string userName = string.Equals(nickname, account, StringComparison.Ordinal) ? account : $"{nickname}（{account}）";
            return $"{userName} · Lv.{auth.CurrentUser.Level}";
        }

        override protected void OnLoginStateChanged(AuthSessionManager.AuthChangeKind kind)
        {
            RefreshControls();
        }

        override protected void RememberRestartReturnToCurrentPage()
        {
            ModSettingsState.SetRestartReturnTarget(ScreenNames.More);
            Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusCommunityScreen", "RecordRestartReturnScreen"), ScreenNames.More));
        }

        private void OpenAboutMod()
        {
            // 原生无本插件 ModEntity（内置编译）：删探测循环与 try/catch，ModInfoDialog 对 null
            // 全容错显示 Unknown + 项目链接；版本行收尾批再定。
            DialogsManager.ShowDialog(null, new ModInfoDialog(null, null, ModIdentity.ProjectPageUrl));
        }

        private static DownloadSourceOption ResolveEffectiveDownloadSource()
        {
            CommunitySettingsManager settings = CommunitySettingsManager.Instance;
            return settings.DownloadSourceSelectionMode == DownloadSourceSelectionMode.CustomDefault
                ? settings.DefaultDownloadSource
                : DownloadSourceOption.DefaultCn;
        }

        private void SetBusy(bool busy)
        {
            m_importButton.IsEnabled = !busy;
            m_downloadQueueButton.IsEnabled = !busy;
        }
    }
}
