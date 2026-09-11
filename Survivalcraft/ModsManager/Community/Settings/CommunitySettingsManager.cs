using System;
using Engine;
using System.Globalization;

namespace Game
{
    /// <summary>
    /// 社区用户设置的运行时缓存：底层存储为原生 SettingsManager.Community* 静态属性
    /// （随 Settings.xml 反射持久化），本类只提供惰性装载、ApplySettingChange 分发，
    /// 以及设置落定后的副作用事件。副作用（切服登出、入口显隐）由 Integration 组合根
    /// 订阅事件接线，本类不直接驱动 Presentation/Integration 控制器。
    /// </summary>
    internal sealed class CommunitySettingsManager
    {
        /// <summary>生产实例（共享服务组合根解散后的唯一接线，同 AuthSessionManager.Instance 惯例）；测试自建实例。</summary>
        internal static readonly CommunitySettingsManager Instance = new();

        public const string PackageName = ModIdentity.PackageName;
        public const string PageId = "General";
        public const string ApiBaseUrlId = "ApiBaseUrl";
        public const string DownloadSourceSelectionModeId = "DownloadSourceSelectionMode";
        public const string DefaultDownloadSourceId = "DefaultDownloadSource";
        public const string ApplyDownloadSourceToUpdatesId = "ApplyDownloadSourceToUpdates";
        public const string ShowImportFromWebLinkEntryId = "ShowImportFromWebLinkEntry";
        public const string PlayedTimeReportingEnabledId = "PlayedTimeReportingEnabled";
        public const string DefaultApiBaseUrl = ApiUrls.DefaultBaseUrl;

        private bool m_loaded;

        public string ApiBaseUrl { get; private set; } = DefaultApiBaseUrl;
        public bool ApplyDownloadSourceToUpdates { get; private set; }
        public bool ShowImportFromWebLinkEntry { get; private set; } = true;
        public bool PlayedTimeReportingEnabled { get; private set; } = true;
        public DownloadSourceSelectionMode DownloadSourceSelectionMode { get; private set; } = DownloadSourceSelectionMode.Default;
        public DownloadSourceOption DefaultDownloadSource { get; private set; } = DownloadSourceOption.DefaultCn;

        // 值落定事件：首次 EnsureLoaded 与每次设置变更都会触发（即便值与现状相同，
        // 与旧 ApplyRuntimeSideEffects 语义一致）。订阅方不得在回调内再触发设置变更。
        public event Action<string> ApiBaseUrlChanged;
        public event Action<bool> ShowImportFromWebLinkEntryChanged;

        public void EnsureLoaded()
        {
            if (m_loaded)
            {
                return;
            }

            m_loaded = true;
            try
            {
                ApiBaseUrl = NormalizeApiBaseUrl(SettingsManager.CommunityApiBaseUrl);
                DownloadSourceSelectionMode = NormalizeSelectionMode(SettingsManager.CommunityDownloadSourceSelectionMode);
                DefaultDownloadSource = NormalizeSource(SettingsManager.CommunityDefaultDownloadSource);
                ApplyDownloadSourceToUpdates = SettingsManager.CommunityApplyDownloadSourceToUpdates;
                ShowImportFromWebLinkEntry = SettingsManager.CommunityShowImportFromWebLinkEntry;
                PlayedTimeReportingEnabled = SettingsManager.CommunityPlayedTimeReportingEnabled;
            }
            catch (Exception ex)
            {
                Log.Error(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusSettingsManager", "LoadNativeModSettingsFailed"), ex.Message));
            }

            NotifyRuntimeEffects();
        }

        public void ApplySettingChange(string[] idPath, object value)
        {
            if (idPath == null || idPath.Length == 0)
            {
                return;
            }

            EnsureLoaded();
            string itemId = idPath[idPath.Length - 1];
            switch (itemId)
            {
                case ApiBaseUrlId when value is string apiBaseUrl:
                    SettingsManager.CommunityApiBaseUrl = NormalizeApiBaseUrl(apiBaseUrl);
                    ApiBaseUrl = SettingsManager.CommunityApiBaseUrl;
                    break;
                case DownloadSourceSelectionModeId when value is DownloadSourceSelectionMode sourceMode:
                    SettingsManager.CommunityDownloadSourceSelectionMode = NormalizeSelectionMode(sourceMode);
                    DownloadSourceSelectionMode = SettingsManager.CommunityDownloadSourceSelectionMode;
                    break;
                case DefaultDownloadSourceId when value is DownloadSourceOption source:
                    SettingsManager.CommunityDefaultDownloadSource = NormalizeSource(source);
                    DefaultDownloadSource = SettingsManager.CommunityDefaultDownloadSource;
                    break;
                case ApplyDownloadSourceToUpdatesId when value is bool apply:
                    SettingsManager.CommunityApplyDownloadSourceToUpdates = apply;
                    ApplyDownloadSourceToUpdates = apply;
                    break;
                case ShowImportFromWebLinkEntryId when value is bool show:
                    SettingsManager.CommunityShowImportFromWebLinkEntry = show;
                    ShowImportFromWebLinkEntry = show;
                    break;
                case PlayedTimeReportingEnabledId when value is bool reporting:
                    SettingsManager.CommunityPlayedTimeReportingEnabled = reporting;
                    PlayedTimeReportingEnabled = reporting;
                    break;
                default:
                    return;
            }

            NotifyRuntimeEffects();
        }

        private void NotifyRuntimeEffects()
        {
            ApiBaseUrlChanged?.Invoke(ApiBaseUrl);
            ShowImportFromWebLinkEntryChanged?.Invoke(ShowImportFromWebLinkEntry);
        }

        public static string NormalizeApiBaseUrl(string value)
        {
            return ApiUrls.NormalizeBaseUrl(value);
        }

        private static DownloadSourceSelectionMode NormalizeSelectionMode(DownloadSourceSelectionMode value)
        {
            return Enum.IsDefined(value) ? value : DownloadSourceSelectionMode.Default;
        }

        private static DownloadSourceOption NormalizeSource(DownloadSourceOption value)
        {
            return Enum.IsDefined(value) ? value : DownloadSourceOption.DefaultCn;
        }
    }
}
