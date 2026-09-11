using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Globalization;
using System.Threading;
using Engine;

namespace Game
{
    /// <summary>
    /// “重启后执行”的本地模组动作清单：把用户在 UI 上对已装模组的删除/禁用/启用
    /// 意图落盘为 JSON 清单（local-mod-pending-actions.json），在模组加载器启动或
    /// 导入流程的重启边界由 <see cref="Process"/> 逐条兑现——删除动作删 .scmod
    /// 文件，启用/禁用写引擎 ModsManager.DisabledMods；失败条目保留在清单里，
    /// 下次启动自动重试。清单的 UI 侧内存视图由 ScbbsPlusLocalModPendingActionStore 维护。
    /// </summary>
    internal static class PendingLocalModActions
    {
        private static readonly Lock s_sync = new();
        private static readonly string s_filePath = StoragePaths.LocalModPendingActionsFile;
        private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };

        public static void Save(IReadOnlyDictionary<string, LocalModPendingActionEntry> actions)
        {
            lock (s_sync)
            {
                Write(new LocalModPendingActionsData
                {
                    Actions = new Dictionary<string, LocalModPendingActionEntry>(
                        actions ?? new Dictionary<string, LocalModPendingActionEntry>(),
                        StringComparer.OrdinalIgnoreCase)
                });
            }
        }

        public static LocalModPendingActionsData Load()
        {
            lock (s_sync)
            {
                return Read();
            }
        }

        public static bool SetManualDisabledState(string packageName, string version, bool isDisabled)
        {
            return isDisabled
                ? MarkManualDisabledState(packageName, version)
                : ClearManualDisabledState(packageName, version);
        }

        // includeSettingActions=false 供模组加载器启动早期使用：模组列表尚未重建，
        // 此时只执行文件删除，启用/禁用留待启动主流程处理。
        public static void Process(string reason, bool includeSettingActions = true)
        {
            LocalModPendingActionsData data;
            lock (s_sync)
            {
                data = Read();
            }
            if (data.Actions == null || data.Actions.Count == 0)
            {
                return;
            }

            Dictionary<string, LocalModPendingActionEntry> remaining = new(StringComparer.OrdinalIgnoreCase);
            foreach ((string key, LocalModPendingActionEntry entry) in data.Actions)
            {
                if (string.IsNullOrWhiteSpace(key) || entry == null || entry.ActionType == LocalModPendingActionType.None)
                {
                    continue;
                }
                if (!includeSettingActions && entry.ActionType != LocalModPendingActionType.Delete)
                {
                    remaining[key] = entry;
                    continue;
                }

                try
                {
                    // 锁外执行文件 IO 与 SaveSettings：锁只保护清单读写。
                    ProcessEntry(entry);
                    Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusPendingLocalModActions", "ProcessedPendingModAction"), reason, entry.ActionType, entry.FilePath));
                }
                catch (Exception ex)
                {
                    remaining[key] = entry;
                    Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusPendingLocalModActions", "ProcessPendingModActionFailed"), reason, entry.ActionType, entry.FilePath, ex.Message));
                }
            }

            lock (s_sync)
            {
                // 处理期间并发 Save 的新条目理论上会被覆盖（Process 仅在启动/重启点调用，
                // 与动作产生的并发窗口极小），可接受。
                Write(new LocalModPendingActionsData { Actions = remaining });
            }
        }

        private static LocalModPendingActionsData Read()
        {
            try
            {
                if (!Storage.FileExists(s_filePath))
                {
                    return new LocalModPendingActionsData();
                }

                LocalModPendingActionsData data = JsonFileStore.Read<LocalModPendingActionsData>(s_filePath, s_jsonOptions)
                    ?? new LocalModPendingActionsData();
                if (data.Actions != null)
                {
                    // JSON 反序列化以默认比较器重建字典，初始化器里的 OrdinalIgnoreCase 丢失；
                    // 读取后显式重建，保证键查找大小写不敏感。
                    data.Actions = new Dictionary<string, LocalModPendingActionEntry>(data.Actions, StringComparer.OrdinalIgnoreCase);
                }
                return data;
            }
            catch (Exception ex)
            {
                Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusPendingLocalModActions", "ReadPendingModManifestFailed"), ex.Message));
                return new LocalModPendingActionsData();
            }
        }

        private static void Write(LocalModPendingActionsData data)
        {
            try
            {
                if (data?.Actions == null || data.Actions.Count == 0)
                {
                    if (Storage.FileExists(s_filePath))
                    {
                        Storage.DeleteFile(s_filePath);
                    }
                    return;
                }

                JsonFileStore.Write(s_filePath, data, s_jsonOptions);
            }
            catch (Exception ex)
            {
                Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusPendingLocalModActions", "SavePendingModManifestFailed"), ex.Message));
            }
        }

        private static void ProcessEntry(LocalModPendingActionEntry entry)
        {
            switch (entry.ActionType)
            {
                case LocalModPendingActionType.Delete:
                    DeleteFile(entry);
                    return;
                case LocalModPendingActionType.Disable:
                    SetEnabled(entry, enable: false);
                    return;
                case LocalModPendingActionType.Enable:
                    SetEnabled(entry, enable: true);
                    return;
            }
        }

        private static void DeleteFile(LocalModPendingActionEntry entry)
        {
            string sourcePath = entry?.FilePath?.Trim();
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                return;
            }

            if (Storage.FileExists(sourcePath))
            {
                Storage.DeleteFile(sourcePath);
            }

            if (ClearManualDisabledState(entry.PackageName, entry.Version))
            {
                SettingsManager.SaveSettings();
            }
        }

        private static void SetEnabled(LocalModPendingActionEntry entry, bool enable)
        {
            string packageName = entry?.PackageName?.Trim();
            string version = entry?.Version?.Trim();
            if (string.IsNullOrWhiteSpace(packageName) || string.IsNullOrWhiteSpace(version))
            {
                throw new InvalidOperationException(LanguageControl.Get("ScbbsPlusCommunityScreen", "Local", "LocalMods", "Pending", "MissingModPackageNameOrVersion"));
            }

            bool changed = enable
                ? ClearManualDisabledState(packageName, version)
                : MarkManualDisabledState(packageName, version);
            if (changed)
            {
                SettingsManager.SaveSettings();
            }
        }

        private static bool MarkManualDisabledState(string packageName, string version)
        {
            if (!ModsManager.DisabledMods.TryGetValue(packageName, out HashSet<string> versions))
            {
                versions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                ModsManager.DisabledMods[packageName] = versions;
            }

            return versions.Add(version);
        }

        private static bool ClearManualDisabledState(string packageName, string version)
        {
            if (string.IsNullOrWhiteSpace(packageName)
                || string.IsNullOrWhiteSpace(version)
                || !ModsManager.DisabledMods.TryGetValue(packageName, out HashSet<string> versions)
                || !versions.Remove(version))
            {
                return false;
            }

            if (versions.Count == 0)
            {
                ModsManager.DisabledMods.Remove(packageName);
            }
            return true;
        }
    }
}
