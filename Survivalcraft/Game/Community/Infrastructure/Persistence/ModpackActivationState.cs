using System;
using Engine;

namespace Game
{
    // 全委托 ModpackStateStore 且无实例状态，静态类直供调用（不再经组合根转发）。
    internal static class ModpackActivationState
    {
        public static ActivationStateLoad Load()
        {
            try
            {
                return new ActivationStateLoad
                {
                    State = ModpackStateStore.Load()
                };
            }
            catch (Exception ex)
            {
                return new ActivationStateLoad { Error = ex };
            }
        }

        public static ActivationStateLoad LoadResolved()
        {
            ActivationStateLoad load = Load();
            if (load.Error != null || load.State == null)
            {
                return load;
            }

            try
            {
                ModpackRegistry.Shared.EnsureReady();
                LocalModpackActivationState state = load.State;
                string resolvedPath = TryResolveRecordPath(state);
                bool changed = false;
                if (string.IsNullOrWhiteSpace(resolvedPath) || !FileExists(resolvedPath))
                {
                    state = null;
                    changed = true;
                }
                else if (!string.Equals(resolvedPath, state.RecordPath, StringComparison.OrdinalIgnoreCase))
                {
                    state.RecordPath = resolvedPath;
                    changed = true;
                }

                if (changed)
                {
                    Exception error = Save(state);
                    if (error != null)
                    {
                        load.Error = error;
                        return load;
                    }
                }

                load.State = state;
            }
            catch (Exception ex)
            {
                load.Error = ex;
            }
            return load;
        }

        public static Exception Save(LocalModpackActivationState state)
        {
            try
            {
                ModpackStateStore.Save(state);
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        internal static bool MatchesRecord(LocalModpackRecord record, LocalModpackActivationState state)
        {
            if (record == null || state == null)
            {
                return false;
            }

            string recordPackageName = record.PackageName?.Trim();
            string statePackageName = state.PackageName?.Trim();
            if (!string.IsNullOrWhiteSpace(recordPackageName) && !string.IsNullOrWhiteSpace(statePackageName))
            {
                if (!string.Equals(recordPackageName, statePackageName, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                string stateVersion = state.Version?.Trim();
                return string.IsNullOrWhiteSpace(stateVersion)
                    || string.Equals(record.Version?.Trim(), stateVersion, StringComparison.OrdinalIgnoreCase);
            }

            string recordName = record.Name?.Trim();
            string stateDisplayName = state.DisplayName?.Trim();
            return !string.IsNullOrWhiteSpace(recordName)
                && !string.IsNullOrWhiteSpace(stateDisplayName)
                && string.Equals(recordName, stateDisplayName, StringComparison.OrdinalIgnoreCase);
        }

        private static string TryResolveRecordPath(LocalModpackActivationState state)
        {
            string recordPath = state?.RecordPath?.Trim();
            if (!string.IsNullOrWhiteSpace(recordPath) && FileExists(recordPath))
            {
                return recordPath;
            }

            ModpackRegistry registry = ModpackRegistry.Shared;
            string fileName = Storage.GetFileName(recordPath ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                string migratedPath = Storage.CombinePaths(registry.DirectoryPath, fileName);
                if (FileExists(migratedPath))
                {
                    return migratedPath;
                }
            }

            foreach (string manifestPath in registry.EnumerateManifestFiles())
            {
                try
                {
                    if (MatchesRecord(registry.ReadRecord(manifestPath), state))
                    {
                        return manifestPath;
                    }
                }
                catch
                {
                    // 探测性质：单条记录读取失败即跳过，走默认分支继续探测其余条目。
                }
            }

            return recordPath;
        }

        private static bool FileExists(string path)
        {
            try
            {
                return !string.IsNullOrWhiteSpace(path)
                    && Storage.FileExists(path);
            }
            catch
            {
                // 探测性质：存在性检查失败按不存在处理，走默认分支。
                return false;
            }
        }
    }

    internal sealed class ActivationStateLoad
    {
        public LocalModpackActivationState State { get; set; }
        public Exception Error { get; set; }
    }
}
