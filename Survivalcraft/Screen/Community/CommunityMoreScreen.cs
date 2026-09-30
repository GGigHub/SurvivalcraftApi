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
        private ButtonWidget m_currentServerButton;
        private ButtonWidget m_downloadSourceSelectionModeButton;
        private ButtonWidget m_defaultDownloadSourceButton;
        private ButtonWidget m_applyDownloadSourceToUpdatesButton;
        private ButtonWidget m_playedTimeReportingButton;
        private ButtonWidget m_postDetailsAutoTranslateButton;
        private ButtonWidget m_topBarBackButton;
        private LabelWidget m_statusLabel;

        public CommunityMoreScreen()
        {
            XElement node = ContentManager.Get<XElement>("Screens/CommunityMoreScreen");
            LoadContents(this, node);
            m_accountInfo = Children.Find<LabelWidget>("ScbbsPlusMoreScreen.AccountInfo");
            m_authButton = Children.Find<ButtonWidget>("ScbbsPlusMoreScreen.AuthButton");
            m_myPublishedButton = Children.Find<ButtonWidget>("ScbbsPlusMoreScreen.MyPublishedButton");
            m_importButton = Children.Find<ButtonWidget>("ScbbsPlusMoreScreen.ImportButton");
            m_downloadQueueButton = Children.Find<ButtonWidget>("ScbbsPlusMoreScreen.DownloadQueueButton");
            m_currentServerButton = Children.Find<ButtonWidget>("ScbbsPlusMoreScreen.CurrentServerButton");
            m_downloadSourceSelectionModeButton = Children.Find<ButtonWidget>("ScbbsPlusMoreScreen.DownloadSourceSelectionModeButton");
            m_defaultDownloadSourceButton = Children.Find<ButtonWidget>("ScbbsPlusMoreScreen.DefaultDownloadSourceButton");
            m_applyDownloadSourceToUpdatesButton = Children.Find<ButtonWidget>("ScbbsPlusMoreScreen.ApplyDownloadSourceToUpdatesButton");
            m_playedTimeReportingButton = Children.Find<ButtonWidget>("ScbbsPlusMoreScreen.PlayedTimeReportingButton");
            m_postDetailsAutoTranslateButton = Children.Find<ButtonWidget>("ScbbsPlusMoreScreen.PostDetailsAutoTranslateButton");
            m_topBarBackButton = Children.Find<ButtonWidget>("TopBar.Back");
            m_statusLabel = Children.Find<LabelWidget>("ScbbsPlusMoreScreen.Status");
        }

        public override void Enter(object[] parameters)
        {
            s_current = this;
            RefreshControls();
            RefreshSettingsUi();
            SetStatus(string.IsNullOrWhiteSpace(s_sharedStatus)
                ? LanguageControl.Get("MoreScreen", "StatusReady")
                : s_sharedStatus);
            AcquireRemoteImportHost();
            InstallDownloadFlowCallbacks(
                SetSharedStatus,
                () => SettingsManager.CommunityDownloadSourceManualSelectEnabled,
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
            RefreshDynamicButtonTexts();

            if (m_authButton.IsClicked) AuthSessionManager.Instance.OpenAuthEntry();
            // 我的发布屏入口（模组原挂在原生 ContentScreen 入口行，游戏内收口到 More 屏）。
            if (m_myPublishedButton.IsClicked) ScreensManager.SwitchScreen("CommunityMyPublished");
            if (m_importButton.IsClicked) RemoteImportManager.OpenImportLinkDialog();
            if (m_downloadQueueButton.IsClicked) DownloadFlowManager.Instance.ShowQueueDialog();
            if (m_currentServerButton.IsClicked)
            {
                // 仅选择不导航模式：选择只改当前服务器并关框，留在本屏（导航语义会弄坏返回栈，
                // 见 SelectCommunityServerDialog.m_selectOnly 注释）
                DialogsManager.ShowDialog(null, new SelectCommunityServerDialog(selectOnly: true));
            }

            if (m_downloadSourceSelectionModeButton.IsClicked)
            {
                // 是/否切换：开 = 每次下载弹框选源，关 = 直接用默认下载源
                SettingsManager.CommunityDownloadSourceManualSelectEnabled = !SettingsManager.CommunityDownloadSourceManualSelectEnabled;
                RefreshSettingsUi();
            }
            if (m_defaultDownloadSourceButton.IsClicked)
            {
                // 打开下载源管理框：列表选默认、添加/编辑自建线路（选择模式行仍走枚举选单）
                DialogsManager.ShowDialog(null, new SelectDownloadSourceDialog());
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
            if (m_postDetailsAutoTranslateButton.IsClicked)
            {
                SettingsManager.CommunityPostDetailsAutoTranslateEnabled = !SettingsManager.CommunityPostDetailsAutoTranslateEnabled;
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
                ? LanguageControl.Get("MoreScreen", "LogOut")
                : LanguageControl.Get("MoreScreen", "LogIn");
        }

        /// <summary>
        /// 对话框里改完设置回本屏无回调可挂（对话框关闭不通知宿主），两枚动态文案钮逐帧
        /// 守卫刷新：当前服务器（选择框/编辑框原地改）与默认下载源（下载源管理框可改）。
        /// 值未变零写入。
        /// </summary>
        private void RefreshDynamicButtonTexts()
        {
            string serverText = GetCurrentServerDisplayText();
            if (m_currentServerButton.Text != serverText)
            {
                m_currentServerButton.Text = serverText;
            }
            string defaultSourceText = CommunityDownloadSourceManager.GetDisplayName(CommunityDownloadSourceManager.CurrentDefaultInfo);
            if (m_defaultDownloadSourceButton.Text != defaultSourceText)
            {
                m_defaultDownloadSourceButton.Text = defaultSourceText;
            }
        }

        /// <summary>
        /// 设置区控件即时刷新，直读 SettingsManager.Community* 与下载源管理器当前状态。
        /// </summary>
        private void RefreshSettingsUi()
        {
            m_currentServerButton.Text = GetCurrentServerDisplayText();
            m_downloadSourceSelectionModeButton.Text = SettingsManager.CommunityDownloadSourceManualSelectEnabled ? LanguageControl.Yes : LanguageControl.No;
            m_defaultDownloadSourceButton.Text = CommunityDownloadSourceManager.GetDisplayName(CommunityDownloadSourceManager.CurrentDefaultInfo);
            m_applyDownloadSourceToUpdatesButton.Text = SettingsManager.CommunityApplyDownloadSourceToUpdates ? LanguageControl.Yes : LanguageControl.No;
            m_playedTimeReportingButton.Text = SettingsManager.CommunityPlayedTimeReportingEnabled ? LanguageControl.Yes : LanguageControl.No;
            m_postDetailsAutoTranslateButton.Text = SettingsManager.CommunityPostDetailsAutoTranslateEnabled ? LanguageControl.Yes : LanguageControl.No;
        }

        /// <summary>当前服务器显示名：优先匹配服务器列表中的中文社区条目（含内置与自建），
        /// 无匹配（如手改设置指向未知地址）显示归一化地址本身。</summary>
        private static string GetCurrentServerDisplayText()
        {
            string normalized = CommunityServerManager.CurrentApiBaseUrl;
            foreach (CommunityServerManager.Info info in CommunityServerManager.GetAllInfos())
            {
                if (info.Type == CommunityServerManager.Type.Chinese
                    && string.Equals(CommunityServerManager.NormalizeApiBaseUrl(info.ApiUrl), normalized, StringComparison.OrdinalIgnoreCase))
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
                return LanguageControl.Get("MoreScreen", "NotLoggedIn");
            }
            string account = string.IsNullOrWhiteSpace(auth.CurrentUser.Account)
                ? LanguageControl.Get("MoreScreen", "UnknownAccount")
                : auth.CurrentUser.Account.Trim();
            string nickname = string.IsNullOrWhiteSpace(auth.CurrentUser.Nickname) ? account : auth.CurrentUser.Nickname.Trim();
            string userName = string.Equals(nickname, account, StringComparison.Ordinal) ? account : $"{nickname}（{account}）";
            return $"{userName} · Lv.{auth.CurrentUser.Level}";
        }

        override protected void OnLoginStateChanged(AuthSessionManager.AuthChangeKind kind)
        {
            RefreshControls();
        }

        private static CommunityDownloadSourceManager.Info ResolveEffectiveDownloadSource()
        {
            // 仅剩 CustomDefault 一种自动模式（ManualSelect 走逐次弹框，不经此）：恒取管理器当前默认线路
            return CommunityDownloadSourceManager.CurrentDefaultInfo;
        }

        private void SetBusy(bool busy)
        {
            m_importButton.IsEnabled = !busy;
            m_downloadQueueButton.IsEnabled = !busy;
        }
    }
}
