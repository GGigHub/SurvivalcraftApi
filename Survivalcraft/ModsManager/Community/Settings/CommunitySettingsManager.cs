using System;
using Engine;

namespace Game
{
    /// <summary>
    /// 仅存两个带副作用的设置包装属性：存储在原生 SettingsManager.Community*
    /// （Settings.xml 反射持久化，原生存储无事件机制），值变更事件由包装属性的
    /// setter 触发，订阅方（CommunityApp 组合根）据以驱动切服登出/入口显隐。
    /// 其余四项设置无副作用，调用方直接读写 SettingsManager.Community*，
    /// 读取处的归一化（ApiUrls.NormalizeBaseUrl / Enum.IsDefined 兜底）由调用方内联。
    /// </summary>
    internal static class CommunitySettingsManager
    {
        public static event Action<string> ApiBaseUrlChanged;

        public static event Action<bool> ShowImportFromWebLinkEntryChanged;

        public static string ApiBaseUrl
        {
            get => SettingsManager.CommunityApiBaseUrl;
            set
            {
                SettingsManager.CommunityApiBaseUrl = ApiUrls.NormalizeBaseUrl(value);
                ApiBaseUrlChanged?.Invoke(SettingsManager.CommunityApiBaseUrl);
            }
        }

        public static bool ShowImportFromWebLinkEntry
        {
            get => SettingsManager.CommunityShowImportFromWebLinkEntry;
            set
            {
                SettingsManager.CommunityShowImportFromWebLinkEntry = value;
                ShowImportFromWebLinkEntryChanged?.Invoke(value);
            }
        }
    }
}
