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
        private TextBoxWidget m_apiBaseUrlBox;
        private ButtonWidget m_apiBaseUrlApplyButton;
        private ButtonWidget m_downloadSourceSelectionModeButton;
        private ButtonWidget m_defaultDownloadSourceButton;
        private ButtonWidget m_applyDownloadSourceToUpdatesButton;
        private ButtonWidget m_playedTimeReportingButton;
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
            m_apiBaseUrlBox = Children.Find<TextBoxWidget>("ScbbsPlusMoreScreen.ApiBaseUrlBox");
            m_apiBaseUrlApplyButton = Children.Find<ButtonWidget>("ScbbsPlusMoreScreen.ApiBaseUrlApplyButton");
            m_downloadSourceSelectionModeButton = Children.Find<ButtonWidget>("ScbbsPlusMoreScreen.DownloadSourceSelectionModeButton");
            m_defaultDownloadSourceButton = Children.Find<ButtonWidget>("ScbbsPlusMoreScreen.DefaultDownloadSourceButton");
            m_applyDownloadSourceToUpdatesButton = Children.Find<ButtonWidget>("ScbbsPlusMoreScreen.ApplyDownloadSourceToUpdatesButton");
            m_playedTimeReportingButton = Children.Find<ButtonWidget>("ScbbsPlusMoreScreen.PlayedTimeReportingButton");
            m_topBarBackButton = Children.Find<ButtonWidget>("TopBar.Back");
            m_statusLabel = Children.Find<LabelWidget>("ScbbsPlusMoreScreen.Status");
        }

        public override void Enter(object[] parameters)
        {
            CommunitySettingsManager.Instance.EnsureLoaded();
            s_current = this;
            RefreshControls();
            RefreshSettingsUi();
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
                        CommunitySettingsManager.Instance.ApplySettingChange([CommunitySettingsManager.DownloadSourceSelectionModeId], (DownloadSourceSelectionMode)item);
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
                        CommunitySettingsManager.Instance.ApplySettingChange([CommunitySettingsManager.DefaultDownloadSourceId], (DownloadSourceOption)item);
                        RefreshSettingsUi();
                    }));
            }
            if (m_applyDownloadSourceToUpdatesButton.IsClicked)
            {
                CommunitySettingsManager.Instance.ApplySettingChange(
                    [CommunitySettingsManager.ApplyDownloadSourceToUpdatesId],
                    !CommunitySettingsManager.Instance.ApplyDownloadSourceToUpdates);
                RefreshSettingsUi();
            }
            if (m_playedTimeReportingButton.IsClicked)
            {
                CommunitySettingsManager.Instance.ApplySettingChange(
                    [CommunitySettingsManager.PlayedTimeReportingEnabledId],
                    !CommunitySettingsManager.Instance.PlayedTimeReportingEnabled);
                RefreshSettingsUi();
            }
            if (m_apiBaseUrlApplyButton.IsClicked)
            {
                // TextBoxWidget 无提交型事件，Text 输入过程不触发设置变更；点"应用"才走
                // ApplySettingChange（触发 ApiBaseUrlChanged → 切服登出链）。
                string apiBaseUrl = m_apiBaseUrlBox.Text;
                if (!string.IsNullOrWhiteSpace(apiBaseUrl))
                {
                    CommunitySettingsManager.Instance.ApplySettingChange([CommunitySettingsManager.ApiBaseUrlId], apiBaseUrl);
                    SetStatus(LanguageControl.Get("ScbbsPlusMoreScreen", "SettingsApplied"));
                    RefreshSettingsUi();
                }
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
        /// 设置区控件即时刷新。ApiBaseUrl 用 ChangeTextNoEvent 回填（Text 变化不触发
        /// TextChanged/设置变更）；应用后的按钮文本在每次 ApplySettingChange 后即时同步。
        /// </summary>
        private void RefreshSettingsUi()
        {
            CommunitySettingsManager settings = CommunitySettingsManager.Instance;
            m_apiBaseUrlBox.ChangeTextNoEvent(settings.ApiBaseUrl);
            m_downloadSourceSelectionModeButton.Text = LanguageControl.Get("DownloadSourceSelectionMode", settings.DownloadSourceSelectionMode.ToString());
            m_defaultDownloadSourceButton.Text = LanguageControl.Get("DownloadSourceOption", settings.DefaultDownloadSource.ToString());
            m_applyDownloadSourceToUpdatesButton.Text = settings.ApplyDownloadSourceToUpdates ? LanguageControl.Yes : LanguageControl.No;
            m_playedTimeReportingButton.Text = settings.PlayedTimeReportingEnabled ? LanguageControl.Yes : LanguageControl.No;
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
