using System;
using System.Globalization;
using System.Xml.Linq;
using Engine;

namespace Game
{
    internal sealed class CommunityMoreScreen : CommunityScreenBase
    {
        private static CommunityMoreScreen s_current;
        private static string s_sharedStatus = string.Empty;

        private LabelWidget m_accountInfo;
        private ButtonWidget m_authButton;
        private ButtonWidget m_myPublishedButton;
        private ButtonWidget m_importButton;
        private ButtonWidget m_downloadQueueButton;
        private LabelWidget m_currentServerValue;
        private ButtonWidget m_downloadSourceSelectionModeButton;
        private ButtonWidget m_defaultDownloadSourceButton;
        private ButtonWidget m_applyDownloadSourceToUpdatesButton;
        private ButtonWidget m_playedTimeReportingButton;
        private ButtonWidget m_topBarBackButton;
        private LabelWidget m_statusLabel;

        public CommunityMoreScreen()
        {
            XElement node = ContentManager.Get<XElement>("Screens/ScbbsPlusMoreScreen");
            LoadContents(this, node);
            m_accountInfo = Children.Find<LabelWidget>("ScbbsPlusMoreScreen.AccountInfo");
            m_authButton = Children.Find<ButtonWidget>("ScbbsPlusMoreScreen.AuthButton");
            m_myPublishedButton = Children.Find<ButtonWidget>("ScbbsPlusMoreScreen.MyPublishedButton");
            m_importButton = Children.Find<ButtonWidget>("ScbbsPlusMoreScreen.ImportButton");
            m_downloadQueueButton = Children.Find<ButtonWidget>("ScbbsPlusMoreScreen.DownloadQueueButton");
            m_currentServerValue = Children.Find<LabelWidget>("ScbbsPlusMoreScreen.CurrentServerValue");
            m_downloadSourceSelectionModeButton = Children.Find<ButtonWidget>("ScbbsPlusMoreScreen.DownloadSourceSelectionModeButton");
            m_defaultDownloadSourceButton = Children.Find<ButtonWidget>("ScbbsPlusMoreScreen.DefaultDownloadSourceButton");
            m_applyDownloadSourceToUpdatesButton = Children.Find<ButtonWidget>("ScbbsPlusMoreScreen.ApplyDownloadSourceToUpdatesButton");
            m_playedTimeReportingButton = Children.Find<ButtonWidget>("ScbbsPlusMoreScreen.PlayedTimeReportingButton");
            m_topBarBackButton = Children.Find<ButtonWidget>("TopBar.Back");
            m_statusLabel = Children.Find<LabelWidget>("ScbbsPlusMoreScreen.Status");
        }

        public override void Enter(object[] parameters)
        {
            s_current = this;
            RefreshControls();
            RefreshSettingsUi();
            SetStatus(string.IsNullOrWhiteSpace(s_sharedStatus)
                ? LanguageControl.Get("ScbbsPlusMoreScreen", "StatusReady")
                : s_sharedStatus);
            AcquireRemoteImportHost();
            InstallDownloadFlowCallbacks(
                SetSharedStatus,
                () => SettingsManager.CommunityDownloadSourceSelectionMode,
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
            // 我的发布屏入口（模组原挂在原生 ContentScreen 入口行，游戏内收口到 More 屏）。
            if (m_myPublishedButton.IsClicked) ScreensManager.SwitchScreen("CommunityMyPublished");
            if (m_importButton.IsClicked) RemoteImportManager.OpenImportLinkDialog();
            if (m_downloadQueueButton.IsClicked) DownloadFlowManager.Instance.ShowQueueDialog();

            if (m_downloadSourceSelectionModeButton.IsClicked)
            {
                // 弹出选单（与模组一致；勿用 IndexOf+1 取模循环——本枚举成员 1 起始，
                // 索引空间取模回投枚举会原地打转）。标题复用行标签键。
                DialogsManager.ShowDialog(null, new ListSelectionDialog(
                    LanguageControl.GetContentWidgets("ScbbsPlusMoreScreen", "DownloadSourceSelectionMode"),
                    EnumUtils.GetEnumValues<DownloadSourceSelectionMode>(),
                    60f,
                    item => EnumDisplayName.GetEnumDisplayName((DownloadSourceSelectionMode)item),
                    item =>
                    {
                        SettingsManager.CommunityDownloadSourceSelectionMode = (DownloadSourceSelectionMode)item;
                        RefreshSettingsUi();
                    }));
            }
            if (m_defaultDownloadSourceButton.IsClicked)
            {
                DialogsManager.ShowDialog(null, new ListSelectionDialog(
                    LanguageControl.GetContentWidgets("ScbbsPlusMoreScreen", "DefaultDownloadSource"),
                    EnumUtils.GetEnumValues<DownloadSourceOption>(),
                    60f,
                    item => EnumDisplayName.GetEnumDisplayName((DownloadSourceOption)item),
                    item =>
                    {
                        SettingsManager.CommunityDefaultDownloadSource = (DownloadSourceOption)item;
                        RefreshSettingsUi();
                    }));
            }
            if (m_applyDownloadSourceToUpdatesButton.IsClicked)
            {
                SettingsManager.CommunityApplyDownloadSourceToUpdates = !SettingsManager.CommunityApplyDownloadSourceToUpdates;
                RefreshSettingsUi();
            }
            if (m_playedTimeReportingButton.IsClicked)
            {
                SettingsManager.CommunityPlayedTimeReportingEnabled = !SettingsManager.CommunityPlayedTimeReportingEnabled;
                RefreshSettingsUi();
            }
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

        /// <summary>
        /// 设置区控件即时刷新，直读 SettingsManager.Community*，内联枚举兜底防手改 XML。
        /// </summary>
        private void RefreshSettingsUi()
        {
            m_currentServerValue.Text = GetCurrentServerDisplayText();
            DownloadSourceSelectionMode sourceMode = SettingsManager.CommunityDownloadSourceSelectionMode;
            m_downloadSourceSelectionModeButton.Text = LanguageControl.Get(
                "DownloadSourceSelectionMode",
                (Enum.IsDefined(sourceMode) ? sourceMode : DownloadSourceSelectionMode.Default).ToString());
            DownloadSourceOption downloadSource = SettingsManager.CommunityDefaultDownloadSource;
            m_defaultDownloadSourceButton.Text = LanguageControl.Get(
                "DownloadSourceOption",
                (Enum.IsDefined(downloadSource) ? downloadSource : DownloadSourceOption.DefaultCn).ToString());
            m_applyDownloadSourceToUpdatesButton.Text = SettingsManager.CommunityApplyDownloadSourceToUpdates ? LanguageControl.Yes : LanguageControl.No;
            m_playedTimeReportingButton.Text = SettingsManager.CommunityPlayedTimeReportingEnabled ? LanguageControl.Yes : LanguageControl.No;
        }

        /// <summary>当前服务器显示名：优先匹配服务器列表中的中文社区条目（含内置与自建），
        /// 无匹配（如手改设置指向未知地址）显示归一化地址本身。</summary>
        private static string GetCurrentServerDisplayText()
        {
            string normalized = ApiUrls.NormalizeBaseUrl(SettingsManager.CommunityApiBaseUrl);
            foreach (CommunityServerManager.Info info in CommunityServerManager.GetAllInfos())
            {
                if (info.Type == CommunityServerManager.Type.Chinese
                    && string.Equals(ApiUrls.NormalizeBaseUrl(info.ApiUrl), normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return CommunityServerManager.GetDisplayName(info);
                }
            }
            return normalized;
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

        private static DownloadSourceOption ResolveEffectiveDownloadSource()
        {
            // 枚举成员可非连续，未定义值不会等于任何具名成员，模式无须兜底；
            // 源读取兜底防手改 Settings.xml。
            return SettingsManager.CommunityDownloadSourceSelectionMode == DownloadSourceSelectionMode.CustomDefault
                ? NormalizeSource(SettingsManager.CommunityDefaultDownloadSource)
                : DownloadSourceOption.DefaultCn;
        }

        private static DownloadSourceOption NormalizeSource(DownloadSourceOption value)
        {
            return Enum.IsDefined(value) ? value : DownloadSourceOption.DefaultCn;
        }

        private void SetBusy(bool busy)
        {
            m_importButton.IsEnabled = !busy;
            m_downloadQueueButton.IsEnabled = !busy;
        }
    }
}
