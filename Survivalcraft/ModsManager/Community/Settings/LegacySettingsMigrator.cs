using System;
using System.Linq;
using System.Xml.Linq;
using Engine;
using XmlUtilities;

namespace Game
{
    /// <summary>
    /// 旧独立模组（ScbbsPlus）遗留用户设置的一次性迁移：ModSettings.xml 的 scbbsplus 段
    /// → SettingsManager.Community*（随 Settings.xml 反射持久化）。迁移完成标志存
    /// ModSettingsState（state.json），无论旧段是否存在都置位，保证只跑一次。
    /// AutoUpdateCheckEnabled 随自更新功能一并退役：读出即丢弃，不迁移、不进 UI。
    /// 单项解析失败跳过不中断；整体异常仅记日志（标志未置位，下次启动自动重试）。
    /// 必须在 CommunitySettingsManager 首次 EnsureLoaded 之前执行
    /// （CommunityApp.Initialize 最早，此时 SettingsManager.Initialize 与 ModsManager.Initialize
    /// 均已完成：Settings.xml 默认值已就绪、ModSettingsCache 已填充）。
    /// </summary>
    internal static class LegacySettingsMigrator
    {
        internal static void Run()
        {
            try
            {
                RunInternal();
            }
            catch (Exception ex)
            {
                Log.Warning($"[ScbbsPlus] Legacy settings migration failed: {ex.Message}");
            }
        }

        private static void RunInternal()
        {
            if (ModSettingsState.LegacySettingsMigrated)
            {
                return;
            }

            // 原生安装没有 scbbsplus 的 ModEntity（ModSettingsManager.Get<T> 会抛
            // KeyNotFoundException），必须走 ModSettingsCache 的原始 XElement 读。
            if (!ModSettingsManager.ModSettingsCache.TryGetValue(CommunitySettingsManager.PackageName, out XElement modEl))
            {
                // 没装过旧模组也置位：避免每次启动都扫描缓存。
                ModSettingsState.MarkLegacySettingsMigrated();
                return;
            }

            int migratedCount = 0;
            foreach (XElement item in modEl.Element("DataDrivenSettings")?.Elements("Item") ?? Enumerable.Empty<XElement>())
            {
                if (TryMigrateItem(item))
                {
                    migratedCount++;
                }
            }

            // 段已消费即删除（迁出 0 项也删）。SaveSettings 内部顺带 SaveModSettings，
            // 删段后的 ModSettings.xml 一并落盘。
            ModSettingsManager.ModSettingsCache.Remove(CommunitySettingsManager.PackageName);
            SettingsManager.SaveSettings();
            ModSettingsState.MarkLegacySettingsMigrated();
            Log.Information($"[ScbbsPlus] Legacy settings migrated: {migratedCount} item(s).");
        }

        private static bool TryMigrateItem(XElement item)
        {
            try
            {
                string path = XmlUtils.GetAttributeValue<string>(item, "Path", null);
                string value = XmlUtils.GetAttributeValue<string>(item, "Value", null);
                if (string.IsNullOrEmpty(path) || value == null)
                {
                    return false;
                }

                string itemId = path.Substring(path.LastIndexOf('/') + 1);
                switch (itemId)
                {
                    case CommunitySettingsManager.ApiBaseUrlId:
                        if (value.Length == 0)
                        {
                            return false;
                        }
                        // 归一化由 EnsureLoaded 兜底，这里存原值。
                        SettingsManager.CommunityApiBaseUrl = value;
                        return true;
                    case CommunitySettingsManager.DownloadSourceSelectionModeId:
                        return TryMigrateEnum<DownloadSourceSelectionMode>(item, value,
                            parsed => SettingsManager.CommunityDownloadSourceSelectionMode = parsed);
                    case CommunitySettingsManager.DefaultDownloadSourceId:
                        return TryMigrateEnum<DownloadSourceOption>(item, value,
                            parsed => SettingsManager.CommunityDefaultDownloadSource = parsed);
                    case CommunitySettingsManager.ApplyDownloadSourceToUpdatesId:
                        return TryMigrateBool(value, parsed => SettingsManager.CommunityApplyDownloadSourceToUpdates = parsed);
                    case CommunitySettingsManager.ShowImportFromWebLinkEntryId:
                        return TryMigrateBool(value, parsed => SettingsManager.CommunityShowImportFromWebLinkEntry = parsed);
                    case CommunitySettingsManager.PlayedTimeReportingEnabledId:
                        return TryMigrateBool(value, parsed => SettingsManager.CommunityPlayedTimeReportingEnabled = parsed);
                    default:
                        // AutoUpdateCheckEnabled 等遗留项：读出即丢弃（自更新功能已退役）。
                        return false;
                }
            }
            catch
            {
                // 单项解析失败跳过，不中断迁移。
                return false;
            }
        }

        private static bool TryMigrateEnum<TEnum>(XElement item, string value, Action<TEnum> assign) where TEnum : struct, Enum
        {
            // 以 Item 的 Type 属性校验旧值类型确为本枚举（Type.GetType 对本程序集类型可直接解析）。
            string typeName = XmlUtils.GetAttributeValue<string>(item, "Type", null);
            if (typeName == null || Type.GetType(typeName) != typeof(TEnum))
            {
                return false;
            }

            if (!Enum.TryParse(value, out TEnum parsed))
            {
                return false;
            }

            assign(parsed);
            return true;
        }

        private static bool TryMigrateBool(string value, Action<bool> assign)
        {
            if (!bool.TryParse(value, out bool parsed))
            {
                return false;
            }

            assign(parsed);
            return true;
        }
    }
}
