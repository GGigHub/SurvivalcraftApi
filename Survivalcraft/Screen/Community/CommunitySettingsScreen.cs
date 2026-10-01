using System.Xml.Linq;

namespace Game {
    sealed class CommunitySettingsScreen : CommunityScreenBase {
        static CommunitySettingsScreen s_current;
        static string s_sharedStatus = string.Empty;

        LabelWidget m_accountInfo;
        ButtonWidget m_authButton;
        ButtonWidget m_myPublishedButton;
        ButtonWidget m_importButton;
        ButtonWidget m_downloadQueueButton;
        ButtonWidget m_currentServerButton;
        ButtonWidget m_downloadSourceSelectionModeButton;
        ButtonWidget m_defaultDownloadSourceButton;
        ButtonWidget m_applyDownloadSourceToUpdatesButton;
        ButtonWidget m_playedTimeReportingButton;
        ButtonWidget m_postDetailsAutoTranslateButton;
        ButtonWidget m_topBarBackButton;
        LabelWidget m_statusLabel;

        public CommunitySettingsScreen() {
            XElement node = ContentManager.Get<XElement>("Screens/CommunitySettingsScreen");
            LoadContents(this, node);
            m_accountInfo = Children.Find<LabelWidget>("CommunitySettingsScreen.AccountInfo");
            m_authButton = Children.Find<ButtonWidget>("CommunitySettingsScreen.AuthButton");
            m_myPublishedButton = Children.Find<ButtonWidget>("CommunitySettingsScreen.MyPublishedButton");
            m_importButton = Children.Find<ButtonWidget>("CommunitySettingsScreen.ImportButton");
            m_downloadQueueButton = Children.Find<ButtonWidget>("CommunitySettingsScreen.DownloadQueueButton");
            m_currentServerButton = Children.Find<ButtonWidget>("CommunitySettingsScreen.CurrentServerButton");
            m_downloadSourceSelectionModeButton = Children.Find<ButtonWidget>("CommunitySettingsScreen.DownloadSourceSelectionModeButton");
            m_defaultDownloadSourceButton = Children.Find<ButtonWidget>("CommunitySettingsScreen.DefaultDownloadSourceButton");
            m_applyDownloadSourceToUpdatesButton = Children.Find<ButtonWidget>("CommunitySettingsScreen.ApplyDownloadSourceToUpdatesButton");
            m_playedTimeReportingButton = Children.Find<ButtonWidget>("CommunitySettingsScreen.PlayedTimeReportingButton");
            m_postDetailsAutoTranslateButton = Children.Find<ButtonWidget>("CommunitySettingsScreen.PostDetailsAutoTranslateButton");
            m_topBarBackButton = Children.Find<ButtonWidget>("TopBar.Back");
            m_statusLabel = Children.Find<LabelWidget>("CommunitySettingsScreen.Status");
        }

        public override void Enter(object[] parameters) {
            s_current = this;
            RefreshControls();
            RefreshSettingsUi();
            SetStatus(string.IsNullOrWhiteSpace(s_sharedStatus) ? LanguageControl.Get("CommunitySettingsScreen", "StatusReady") : s_sharedStatus);
            AcquireRemoteImportHost();
            InstallDownloadFlowCallbacks(
                SetSharedStatus,
                () => SettingsManager.CommunityDownloadSourceManualSelectEnabled,
                ResolveEffectiveDownloadSource
            );
        }

        public override void Leave() {
            ReleaseDownloadFlowCallbacks(true);
            DownloadFlowManager controller = DownloadFlowManager.Instance;
            if (controller.StatusReporter == (Action<string>)SetSharedStatus) {
                controller.StatusReporter = null;
            }
            if (s_current == this) {
                s_current = null;
            }
        }

        public override void Update() {
            if (Input.Back
                || Input.Cancel
                || m_topBarBackButton.IsClicked) {
                ScreensManager.GoBack();
                return;
            }
            RefreshDynamicButtonTexts();
            if (m_authButton.IsClicked) {
                AuthSessionManager.Instance.OpenAuthEntry();
            }
            // 我的发布屏入口（模组原挂在原生 ContentScreen 入口行，游戏内收口到 More 屏）。
            if (m_myPublishedButton.IsClicked) {
                ScreensManager.SwitchScreen("CommunityMyPublished");
            }
            if (m_importButton.IsClicked) {
                RemoteImportManager.OpenImportLinkDialog();
            }
            if (m_downloadQueueButton.IsClicked) {
                DownloadFlowManager.Instance.ShowQueueDialog();
            }
            if (m_currentServerButton.IsClicked) {
                // 仅选择不导航模式：选择只改当前服务器并关框，留在本屏（导航语义会弄坏返回栈，
                // 见 SelectCommunityServerDialog.m_selectOnly 注释）
                DialogsManager.ShowDialog(null, new SelectCommunityServerDialog(true));
            }
            if (m_downloadSourceSelectionModeButton.IsClicked) {
                // 是/否切换：开 = 每次下载弹框选源，关 = 直接用默认下载源
                SettingsManager.CommunityDownloadSourceManualSelectEnabled = !SettingsManager.CommunityDownloadSourceManualSelectEnabled;
                RefreshSettingsUi();
            }
            if (m_defaultDownloadSourceButton.IsClicked) {
                // 打开下载源管理框：列表选默认、添加/编辑自建线路（选择模式行仍走枚举选单）
                DialogsManager.ShowDialog(null, new SelectDownloadSourceDialog());
            }
            if (m_applyDownloadSourceToUpdatesButton.IsClicked) {
                SettingsManager.CommunityApplyDownloadSourceToUpdates = !SettingsManager.CommunityApplyDownloadSourceToUpdates;
                RefreshSettingsUi();
            }
            if (m_playedTimeReportingButton.IsClicked) {
                SettingsManager.CommunityPlayedTimeReportingEnabled = !SettingsManager.CommunityPlayedTimeReportingEnabled;
                RefreshSettingsUi();
            }
            if (m_postDetailsAutoTranslateButton.IsClicked) {
                SettingsManager.CommunityPostDetailsAutoTranslateEnabled = !SettingsManager.CommunityPostDetailsAutoTranslateEnabled;
                RefreshSettingsUi();
            }
        }

        internal static void SetSharedStatus(string status) {
            s_sharedStatus = status ?? string.Empty;
            s_current?.SetStatus(s_sharedStatus);
        }

        void SetStatus(string status) {
            s_sharedStatus = status ?? string.Empty;
            m_statusLabel.Text = s_sharedStatus;
        }

        void RefreshControls() {
            m_accountInfo.Text = GetAccountInfoText();
            m_authButton.Text = AuthSessionManager.Instance.IsLoggedIn
                ? LanguageControl.Get("CommunitySettingsScreen", "LogOut")
                : LanguageControl.Get("CommunitySettingsScreen", "LogIn");
        }

        /// <summary>
        /// 对话框里改完设置回本屏无回调可挂（对话框关闭不通知宿主），两枚动态文案钮逐帧
        /// 守卫刷新：当前服务器（选择框/编辑框原地改）与默认下载源（下载源管理框可改）。
        /// 值未变零写入。
        /// </summary>
        void RefreshDynamicButtonTexts() {
            string serverText = GetCurrentServerDisplayText();
            if (m_currentServerButton.Text != serverText) {
                m_currentServerButton.Text = serverText;
            }
            string defaultSourceText = CommunityDownloadSourceManager.GetDisplayName(CommunityDownloadSourceManager.CurrentDefaultInfo);
            if (m_defaultDownloadSourceButton.Text != defaultSourceText) {
                m_defaultDownloadSourceButton.Text = defaultSourceText;
            }
        }

        /// <summary>
        /// 设置区控件即时刷新，直读 SettingsManager.Community* 与下载源管理器当前状态。
        /// </summary>
        void RefreshSettingsUi() {
            m_currentServerButton.Text = GetCurrentServerDisplayText();
            m_downloadSourceSelectionModeButton.Text = SettingsManager.CommunityDownloadSourceManualSelectEnabled
                ? LanguageControl.Yes
                : LanguageControl.No;
            m_defaultDownloadSourceButton.Text = CommunityDownloadSourceManager.GetDisplayName(CommunityDownloadSourceManager.CurrentDefaultInfo);
            m_applyDownloadSourceToUpdatesButton.Text = SettingsManager.CommunityApplyDownloadSourceToUpdates
                ? LanguageControl.Yes
                : LanguageControl.No;
            m_playedTimeReportingButton.Text = SettingsManager.CommunityPlayedTimeReportingEnabled ? LanguageControl.Yes : LanguageControl.No;
            m_postDetailsAutoTranslateButton.Text = SettingsManager.CommunityPostDetailsAutoTranslateEnabled
                ? LanguageControl.Yes
                : LanguageControl.No;
        }

        /// <summary>
        /// 当前服务器显示名：优先匹配服务器列表中的中文社区条目（含内置与自建），
        /// 无匹配（如手改设置指向未知地址）显示归一化地址本身。
        /// </summary>
        static string GetCurrentServerDisplayText() {
            string normalized = CommunityServerManager.CurrentApiBaseUrl;
            foreach (CommunityServerManager.Info info in CommunityServerManager.GetAllInfos()) {
                if (info.Type == CommunityServerManager.Type.Chinese
                    && string.Equals(CommunityServerManager.NormalizeApiBaseUrl(info.ApiUrl), normalized, StringComparison.OrdinalIgnoreCase)) {
                    return CommunityServerManager.GetDisplayName(info);
                }
            }
            return normalized;
        }

        string GetAccountInfoText() {
            AuthSessionManager auth = AuthSessionManager.Instance;
            if (!auth.IsLoggedIn
                || auth.CurrentUser == null) {
                return LanguageControl.Get("CommunitySettingsScreen", "NotLoggedIn");
            }
            string account = string.IsNullOrWhiteSpace(auth.CurrentUser.Account)
                ? LanguageControl.Get("CommunitySettingsScreen", "UnknownAccount")
                : auth.CurrentUser.Account.Trim();
            string nickname = string.IsNullOrWhiteSpace(auth.CurrentUser.Nickname) ? account : auth.CurrentUser.Nickname.Trim();
            string userName = string.Equals(nickname, account, StringComparison.Ordinal) ? account : $"{nickname}（{account}）";
            return $"{userName} · Lv.{auth.CurrentUser.Level}";
        }

        protected override void OnLoginStateChanged(AuthSessionManager.AuthChangeKind kind) {
            RefreshControls();
        }

        static CommunityDownloadSourceManager.Info ResolveEffectiveDownloadSource() =>
            // 仅剩 CustomDefault 一种自动模式（ManualSelect 走逐次弹框，不经此）：恒取管理器当前默认线路
            CommunityDownloadSourceManager.CurrentDefaultInfo;

        void SetBusy(bool busy) {
            m_importButton.IsEnabled = !busy;
            m_downloadQueueButton.IsEnabled = !busy;
        }
    }
}