using System;
using Engine;
using System.Globalization;

namespace Game
{
    /// <summary>
    /// 原生模组设置的运行时缓存。持久化由 ModSettingsManager 负责；此类只提供
    /// 热路径读取、ModLoader.OnModSettingChanged 的变更分发，以及设置落定后的
    /// 事件。副作用（登出、自动更新开关、入口显隐）由 Integration 组合根订阅
    /// 事件接线，本类不直接驱动 Presentation/Integration 控制器。
    /// </summary>
    internal sealed class CommunitySettingsManager
    {
        /// <summary>生产实例（共享服务组合根解散后的唯一接线，同 AuthSessionManager.Instance 惯例）；测试自建实例。</summary>
        internal static readonly CommunitySettingsManager Instance = new();

        public const string PackageName = ModIdentity.PackageName;
        public const string PageId = "General";
        public const string ApiBaseUrlId = "ApiBaseUrl";
        public const string AutoUpdateCheckId = "AutoUpdateCheckEnabled";
        public const string DownloadSourceSelectionModeId = "DownloadSourceSelectionMode";
        public const string DefaultDownloadSourceId = "DefaultDownloadSource";
        public const string ApplyDownloadSourceToUpdatesId = "ApplyDownloadSourceToUpdates";
        public const string ShowImportFromWebLinkEntryId = "ShowImportFromWebLinkEntry";
        public const string PlayedTimeReportingEnabledId = "PlayedTimeReportingEnabled";
        public const string DefaultApiBaseUrl = ApiUrls.DefaultBaseUrl;

        private bool m_loaded;

        public string ApiBaseUrl { get; private set; } = DefaultApiBaseUrl;
        public bool AutoUpdateCheckEnabled { get; private set; } = true;
        public bool ApplyDownloadSourceToUpdates { get; private set; }
        public bool ShowImportFromWebLinkEntry { get; private set; } = true;
        public bool PlayedTimeReportingEnabled { get; private set; } = true;
        public DownloadSourceSelectionMode DownloadSourceSelectionMode { get; private set; } = DownloadSourceSelectionMode.Default;
        public DownloadSourceOption DefaultDownloadSource { get; private set; } = DownloadSourceOption.DefaultCn;

        // 值落定事件：首次 EnsureLoaded 与每次设置变更都会触发（即便值与现状相同，
        // 与旧 ApplyRuntimeSideEffects 语义一致）。订阅方不得在回调内再触发设置变更。
        public event Action<string> ApiBaseUrlChanged;
        public event Action<bool> AutoUpdateCheckEnabledChanged;
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
                ApiBaseUrl = NormalizeApiBaseUrl(ModSettingsManager.Get<string>(PackageName, PageId, ApiBaseUrlId));
                AutoUpdateCheckEnabled = ModSettingsManager.Get<bool>(PackageName, PageId, AutoUpdateCheckId);
                DownloadSourceSelectionMode = NormalizeSelectionMode(ModSettingsManager.Get<DownloadSourceSelectionMode>(PackageName, PageId, DownloadSourceSelectionModeId));
                DefaultDownloadSource = NormalizeSource(ModSettingsManager.Get<DownloadSourceOption>(PackageName, PageId, DefaultDownloadSourceId));
                ApplyDownloadSourceToUpdates = ModSettingsManager.Get<bool>(PackageName, PageId, ApplyDownloadSourceToUpdatesId);
                ShowImportFromWebLinkEntry = ModSettingsManager.Get<bool>(PackageName, PageId, ShowImportFromWebLinkEntryId);
                PlayedTimeReportingEnabled = ModSettingsManager.Get<bool>(PackageName, PageId, PlayedTimeReportingEnabledId);
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
                    ApiBaseUrl = NormalizeApiBaseUrl(apiBaseUrl);
                    break;
                case AutoUpdateCheckId when value is bool autoUpdate:
                    AutoUpdateCheckEnabled = autoUpdate;
                    break;
                case DownloadSourceSelectionModeId when value is DownloadSourceSelectionMode sourceMode:
                    DownloadSourceSelectionMode = NormalizeSelectionMode(sourceMode);
                    break;
                case DefaultDownloadSourceId when value is DownloadSourceOption source:
                    DefaultDownloadSource = NormalizeSource(source);
                    break;
                case ApplyDownloadSourceToUpdatesId when value is bool apply:
                    ApplyDownloadSourceToUpdates = apply;
                    break;
                case ShowImportFromWebLinkEntryId when value is bool show:
                    ShowImportFromWebLinkEntry = show;
                    break;
                case PlayedTimeReportingEnabledId when value is bool reporting:
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
            AutoUpdateCheckEnabledChanged?.Invoke(AutoUpdateCheckEnabled);
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
