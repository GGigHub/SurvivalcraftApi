using System.Globalization;
using Engine;

namespace Game {
    static class ModpackDataFiles {
        static readonly HashSet<string> s_protectedRootNames = new(StringComparer.OrdinalIgnoreCase) {
            "project.xml",
            "saves",
            "mods",
            "settings",
            "modpacks",
            "worlds",
            "texturepacks",
            "characterskins",
            "furniturepacks",
            "shadercaches",
            "Settings.xml",
            "ModSettings.xml",
            "Configs.xml",
            "UserId.dat",
            "CommunityContentCache.xml"
        };

        public static void Import(byte[] data, ModpackItem item) {
            if (item == null
                || (string.IsNullOrWhiteSpace(item.TargetPath) && string.IsNullOrWhiteSpace(item.TargetDirectory))) {
                throw new InvalidOperationException(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        LanguageControl.Get("ModpackDataFiles", "DataFileHasNoTargetPathSpecified"),
                        item?.Filename
                    )
                );
            }
            string targetPath = ResolveTargetPath(item);
            string targetDirectory = ModpackRegistry.Shared.GetParentDirectory(targetPath);
            if (!string.IsNullOrWhiteSpace(targetDirectory)
                && !Storage.DirectoryExists(targetDirectory)) {
                Storage.CreateDirectory(targetDirectory);
            }
            string temporaryDirectory = string.IsNullOrWhiteSpace(targetDirectory) ? CommunityStoragePaths.RootDirectory : targetDirectory;
            string temporaryPath = Storage.CombinePaths(temporaryDirectory, $".-import-{Guid.NewGuid():N}.tmp");
            try {
                ModpackRegistry.Shared.WriteBytes(temporaryPath, data);
                // 模组走 TransactionalFileReplacement.Replace（Delete+Move），本体按 brief 改用
                // Storage.MoveFileSafely（JsonFileStore.Write 同款先例）；Safely 不容忍源缺失（会抛），
                // 其安全点是目标存在时先备份、失败回滚备份并重抛原始异常、不吞错；tmp 必在，故源缺失分支不可达。
                Storage.MoveFileSafely(temporaryPath, targetPath);
            }
            finally {
                try {
                    if (Storage.FileExists(temporaryPath)) {
                        Storage.DeleteFile(temporaryPath);
                    }
                }
                catch (Exception ex) {
                    Log.Warning(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            LanguageControl.Get("ModpackDataFiles", "CleanModpackCustomTempFileFailed"),
                            temporaryPath,
                            ex.Message
                        )
                    );
                }
            }
        }

        public static bool IsPresent(ModpackItem item) {
            try {
                return Storage.FileExists(ResolveTargetPath(item));
            }
            catch {
                return false;
            }
        }

        public static byte[] PrepareForActivation(byte[] packagedData,
            ModpackItem item,
            List<LocalModpackFileStateEntry> backups,
            string persistenceKey) {
            string targetPath = ResolveTargetPath(item);
            LocalModpackFileStateEntry existingEntry = backups.FirstOrDefault(entry => string.Equals(
                    entry.TargetPath,
                    targetPath,
                    StringComparison.OrdinalIgnoreCase
                )
            );
            if (existingEntry != null) {
                return !string.IsNullOrWhiteSpace(existingEntry.PersistedPath) && Storage.FileExists(existingEntry.PersistedPath)
                    ? ModpackRegistry.Shared.ReadBytes(existingEntry.PersistedPath)
                    : packagedData;
            }
            bool existedBefore = Storage.FileExists(targetPath);
            string backupPath = null;
            if (existedBefore) {
                backupPath = Storage.CombinePaths(CommunityStoragePaths.ModpackBackupsDirectory, $"{Guid.NewGuid():N}.bak");
                ModpackRegistry.Shared.WriteBytes(backupPath, ModpackRegistry.Shared.ReadBytes(targetPath));
            }
            string persistedPath = BuildPersistedPath(persistenceKey, targetPath);
            backups.Add(
                new LocalModpackFileStateEntry {
                    TargetPath = targetPath, BackupPath = backupPath, PersistedPath = persistedPath, ExistedBefore = existedBefore
                }
            );
            return Storage.FileExists(persistedPath) ? ModpackRegistry.Shared.ReadBytes(persistedPath) : packagedData;
        }

        public static bool TryPersistChanges(IEnumerable<LocalModpackFileStateEntry> backups, string persistenceKey, out string error) {
            error = null;
            foreach (LocalModpackFileStateEntry entry in backups ?? []) {
                if (entry == null
                    || string.IsNullOrWhiteSpace(entry.TargetPath)) {
                    continue;
                }
                entry.PersistedPath ??= BuildPersistedPath(persistenceKey, entry.TargetPath);
                try {
                    if (Storage.FileExists(entry.TargetPath)) {
                        ModpackRegistry.Shared.WriteBytes(entry.PersistedPath, ModpackRegistry.Shared.ReadBytes(entry.TargetPath));
                    }
                    else if (Storage.FileExists(entry.PersistedPath)) {
                        Storage.DeleteFile(entry.PersistedPath);
                    }
                }
                catch (Exception ex) {
                    Log.Error(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            LanguageControl.Get("ModpackDataFiles", "SaveModpackCustomConfigFailed"),
                            entry.TargetPath,
                            entry.PersistedPath,
                            ex
                        )
                    );
                    error = $"{entry.TargetPath}：{ex.Message}";
                    return false;
                }
            }
            return true;
        }

        public static bool TryRestoreBackups(IEnumerable<LocalModpackFileStateEntry> backups, out string error) {
            error = null;
            IReadOnlyList<LocalModpackFileStateEntry> entries = [.. (backups ?? [])
                .Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.TargetPath))];
            foreach (LocalModpackFileStateEntry entry in entries) {
                if (entry.ExistedBefore
                    && (string.IsNullOrWhiteSpace(entry.BackupPath) || !Storage.FileExists(entry.BackupPath))) {
                    error = string.Format(
                        CultureInfo.InvariantCulture,
                        LanguageControl.Get("ModpackDataFiles", "BackupFileDoesNotExist"),
                        entry.BackupPath
                    );
                    return false;
                }
            }
            foreach (LocalModpackFileStateEntry entry in entries) {
                try {
                    if (entry.ExistedBefore) {
                        ModpackRegistry.Shared.WriteBytes(entry.TargetPath, ModpackRegistry.Shared.ReadBytes(entry.BackupPath));
                    }
                    else if (Storage.FileExists(entry.TargetPath)) {
                        Storage.DeleteFile(entry.TargetPath);
                    }
                }
                catch (Exception ex) {
                    Log.Error(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            LanguageControl.Get("ModpackDataFiles", "RestoreModpackCustomFileFailed"),
                            entry.TargetPath,
                            entry.BackupPath,
                            ex
                        )
                    );
                    error = $"{entry.TargetPath}：{ex.Message}";
                    return false;
                }
            }

            // 恢复已成功，清理备份失败不应让整个恢复报失败（与上方逐条守卫的循环保持一致）。
            foreach (LocalModpackFileStateEntry entry in entries) {
                if (!string.IsNullOrWhiteSpace(entry.BackupPath)
                    && Storage.FileExists(entry.BackupPath)) {
                    try {
                        Storage.DeleteFile(entry.BackupPath);
                    }
                    catch (Exception ex) {
                        Log.Information(
                            string.Format(
                                CultureInfo.InvariantCulture,
                                LanguageControl.Get("ModpackDataFiles", "DeleteModpackBackupFailed"),
                                entry.BackupPath,
                                ex.Message
                            )
                        );
                    }
                }
            }
            return true;
        }

        public static string ResolveTargetPath(ModpackItem item) {
            if (!string.IsNullOrWhiteSpace(item?.TargetPath)) {
                return ResolveTargetPath(item.TargetPath);
            }
            string targetDirectory = item?.TargetDirectory?.Trim();
            if (string.IsNullOrWhiteSpace(targetDirectory)) {
                throw new InvalidOperationException(LanguageControl.Get("ModpackDataFiles", "DataFileHasNoTargetPathSpecifiedPlain"));
            }
            bool isDirectoryTarget = targetDirectory.EndsWith("/", StringComparison.Ordinal)
                || targetDirectory.EndsWith("\\", StringComparison.Ordinal);
            if (!isDirectoryTarget) {
                return ResolveTargetPath(targetDirectory);
            }
            string sourceFileName = item.HasExplicitFileName
                ? Storage.GetFileName(item.ManifestFileName ?? item.Filename ?? string.Empty)
                : ModpackRules.TryGetPathTailName(item.Url);
            if (string.IsNullOrWhiteSpace(sourceFileName)) {
                throw new InvalidOperationException(LanguageControl.Get("ModpackDataFiles", "CustomFileUrlHasNoFileName"));
            }
            return ResolveTargetPath($"{targetDirectory}{sourceFileName}");
        }

        public static string ResolveTargetPath(string targetPath) {
            string safeRelativePath = SanitizeRelativePath(targetPath);
            if (string.IsNullOrWhiteSpace(safeRelativePath)) {
                throw new InvalidOperationException(LanguageControl.Get("ModpackDataFiles", "DataFileHasNoTargetPathSpecifiedPlain"));
            }
            string result = GetBasePath();
            foreach (string segment in safeRelativePath.Split('/')) {
                if (!string.IsNullOrWhiteSpace(segment)) {
                    result = Storage.CombinePaths(result, segment);
                }
            }
            return result;
        }

        internal static string SanitizeRelativePath(string targetPath) {
            string normalized = (targetPath ?? string.Empty).Trim().Replace('\\', '/');
            int colonIndex = normalized.IndexOf(':');
            if (colonIndex >= 0) {
                normalized = normalized[(colonIndex + 1)..];
            }
            List<string> safeSegments = [];
            foreach (string rawSegment in normalized.Split('/', StringSplitOptions.RemoveEmptyEntries)) {
                string segment = rawSegment.Trim();
                if (string.IsNullOrWhiteSpace(segment)
                    || segment == "."
                    || segment == "..") {
                    continue;
                }

                // 本体无 CommunityUploadFileNaming.SanitizeSegment；已核实 Storage.SanitizeFileNameSegment
                // 与模组实现语义一致（非法字符/引号分隔符替换、尾点空格剥离、保留名加前缀、200 字符截断）。
                segment = Storage.SanitizeFileNameSegment(segment).TrimEnd('.', ' ');
                if (!string.IsNullOrWhiteSpace(segment)
                    && segment != "."
                    && segment != "..") {
                    safeSegments.Add(segment);
                }
            }
            if (safeSegments.Count == 0) {
                return string.Empty;
            }
            if (s_protectedRootNames.Contains(safeSegments[0])) {
                throw new InvalidOperationException(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        LanguageControl.Get("ModpackDataFiles", "UnsafeCustomFileTargetPathRejected"),
                        targetPath
                    )
                );
            }
            return string.Join("/", safeSegments);
        }

        static string GetBasePath() {
            string localModpacksParent = ModpackRegistry.Shared.GetParentDirectory(ModpackRegistry.Shared.DirectoryPath);
            if (!string.IsNullOrWhiteSpace(localModpacksParent)
                && Storage.DirectoryExists(localModpacksParent)) {
                return localModpacksParent;
            }
            string documentPath = (ModsManager.DocPath ?? string.Empty).TrimEnd('/', '\\');
            if (string.IsNullOrWhiteSpace(documentPath)
                || Storage.DirectoryExists(documentPath)) {
                return documentPath;
            }
            string parentPath = ModpackRegistry.Shared.GetParentDirectory(documentPath);
            return !string.IsNullOrWhiteSpace(parentPath) && Storage.DirectoryExists(parentPath) ? parentPath : documentPath;
        }

        static string BuildPersistedPath(string persistenceKey, string targetPath) {
            string packName = Storage.SanitizeFileNameSegment(Storage.GetFileNameWithoutExtensionBCL(persistenceKey ?? string.Empty));
            if (string.IsNullOrWhiteSpace(packName)) {
                packName = "pack";
            }
            uint hash = 2166136261;
            foreach (char ch in (targetPath ?? string.Empty).ToLowerInvariant()) {
                hash ^= ch;
                hash *= 16777619;
            }
            string fileName = Storage.GetFileName(targetPath ?? string.Empty);
            return Storage.CombinePaths(CommunityStoragePaths.RootDirectory, "integration-pack-custom-files", packName, $"{hash:x8}_{fileName}");
        }
    }
}