using System;
using System.Collections.Generic;
using System.Linq;
using Engine;
using System.Globalization;

namespace Game
{
    internal static class TransactionalFileReplacement
    {
        // 备份文件名格式：<原文件名>.scbbsplus-backup-<32位hex>，游戏不会误加载，且可在启动时按此格式识别并回滚。
        internal const string BackupMarker = ".scbbsplus-backup-";
        private const int BackupGuidLength = 32;

        // 启动时回滚上次更新中断留下的残留备份：原文件缺失说明旧文件只被移到了备份路径，移回原路径；
        // 原文件仍在说明该文件未被移走或新文件已就位，直接清理备份。
        public static void RecoverLeftoverBackups(params string[] rootPaths)
        {
            if (rootPaths == null || rootPaths.Length == 0)
            {
                return;
            }

            HashSet<string> visitedDirectories = new(StringComparer.OrdinalIgnoreCase);
            foreach (string rootPath in rootPaths)
            {
                if (string.IsNullOrWhiteSpace(rootPath))
                {
                    continue;
                }

                try
                {
                    if (Storage.DirectoryExists(rootPath))
                    {
                        RecoverInDirectory(rootPath, visitedDirectories, isRoot: true);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusTransactionalFileReplacement", "ScanResidualBackupsFailed"), rootPath, ex.Message));
                }
            }
        }

        private static void RecoverInDirectory(string directoryPath, HashSet<string> visitedDirectories, bool isRoot)
        {
            if (!visitedDirectories.Add(directoryPath))
            {
                return;
            }

            foreach (string fileName in Storage.ListFileNames(directoryPath).ToArray())
            {
                if (!TryParseBackupFileName(fileName, out string originalFileName))
                {
                    continue;
                }

                string backupPath = Storage.CombinePaths(directoryPath, fileName);
                string originalPath = Storage.CombinePaths(directoryPath, originalFileName);
                try
                {
                    if (Storage.FileExists(originalPath))
                    {
                        Storage.DeleteFile(backupPath);
                        Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusTransactionalFileReplacement", "CleanResidualBackup"), backupPath));
                    }
                    else
                    {
                        Storage.MoveFile(backupPath, originalPath);
                        Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusTransactionalFileReplacement", "RollbackResidualBackup"), backupPath, originalPath));
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusTransactionalFileReplacement", "ProcessResidualBackupFailed"), backupPath, ex.Message));
                }
            }

            foreach (string directoryName in Storage.ListDirectoryNames(directoryPath))
            {
                // 跳过名单只在扫描根层生效：模组更新写 ModsPath 根（ModsPath 即 DocPath/Mods 时
                // 由 ModsPath 根扫描覆盖，visitedDirectories 已去重）；整合包数据文件写 DocPath 下
                // 非受保护根名的子目录（含以受保护根名命名的深层子目录，如 <包目录>/Mods/x），
                // 故深层目录不跳过。
                if (isRoot && IsSkippedRootDirectory(directoryName))
                {
                    continue;
                }

                try
                {
                    RecoverInDirectory(Storage.CombinePaths(directoryPath, directoryName), visitedDirectories, isRoot: false);
                }
                catch (Exception ex)
                {
                    Log.Warning(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusTransactionalFileReplacement", "ScanResidualBackupSubdirectoryFailed"), directoryName, ex.Message));
                }
            }
        }

        // 与 ScbbsPlusModpackDataFiles.s_protectedRootNames 对齐：
        // 这些根目录不可能出现本模组备份（数据文件被拒写，模组更新由 ModsPath 根扫描覆盖）。
        private static bool IsSkippedRootDirectory(string directoryName)
        {
            return string.Equals(directoryName, "Saves", StringComparison.OrdinalIgnoreCase)
                || string.Equals(directoryName, "Worlds", StringComparison.OrdinalIgnoreCase)
                || string.Equals(directoryName, "ShaderCaches", StringComparison.OrdinalIgnoreCase)
                || string.Equals(directoryName, "Mods", StringComparison.OrdinalIgnoreCase)
                || string.Equals(directoryName, "Settings", StringComparison.OrdinalIgnoreCase)
                || string.Equals(directoryName, "ModPacks", StringComparison.OrdinalIgnoreCase)
                || string.Equals(directoryName, "TexturePacks", StringComparison.OrdinalIgnoreCase)
                || string.Equals(directoryName, "CharacterSkins", StringComparison.OrdinalIgnoreCase)
                || string.Equals(directoryName, "FurniturePacks", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryParseBackupFileName(string fileName, out string originalFileName)
        {
            originalFileName = null;
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return false;
            }

            // 备份名格式 <原文件名><marker><32位hex>：marker 不在末尾，用 LastIndexOf 定位。
            int markerIndex = fileName.LastIndexOf(BackupMarker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex <= 0
                || fileName.Length - markerIndex - BackupMarker.Length != BackupGuidLength)
            {
                return false;
            }

            for (int i = markerIndex + BackupMarker.Length; i < fileName.Length; i++)
            {
                if (!IsHexChar(fileName[i]))
                {
                    return false;
                }
            }

            originalFileName = fileName.Substring(0, markerIndex);
            return true;
        }

        private static bool IsHexChar(char ch)
        {
            return (ch >= '0' && ch <= '9')
                || (ch >= 'a' && ch <= 'f')
                || (ch >= 'A' && ch <= 'F');
        }

        // 先把所有旧文件移到非模组扩展名的备份路径，只有新文件就位后才清理备份。
        public static void Replace(string tempPath, string targetPath, params string[] oldPaths)
        {
            List<FileBackup> backups = new();
            HashSet<string> pathsToBackup = new(StringComparer.OrdinalIgnoreCase);
            bool installMoveStarted = false;
            bool installed = false;

            pathsToBackup.Add(targetPath);
            if (oldPaths != null)
            {
                foreach (string oldPath in oldPaths)
                {
                    if (!string.IsNullOrWhiteSpace(oldPath))
                    {
                        pathsToBackup.Add(oldPath);
                    }
                }
            }

            try
            {
                foreach (string oldPath in pathsToBackup)
                {
                    if (!Storage.FileExists(oldPath))
                    {
                        continue;
                    }

                    string backupPath = $"{oldPath}{BackupMarker}{Guid.NewGuid():N}";
                    Storage.MoveFile(oldPath, backupPath);
                    backups.Add(new FileBackup(oldPath, backupPath));
                }

                installMoveStarted = true;
                Storage.MoveFile(tempPath, targetPath);
                installed = true;
            }
            finally
            {
                if (installed)
                {
                    DeleteBackups(backups);
                }
                else
                {
                    RollBack(targetPath, installMoveStarted, backups);
                }
            }
        }

        private static void DeleteBackups(List<FileBackup> backups)
        {
            foreach (FileBackup backup in backups)
            {
                try
                {
                    if (Storage.FileExists(backup.BackupPath))
                    {
                        Storage.DeleteFile(backup.BackupPath);
                    }
                }
                catch (Exception ex)
                {
                    Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusTransactionalFileReplacement", "CleanModUpdateBackupFailed"), ex.Message));
                }
            }
        }

        private static void RollBack(string targetPath, bool installMoveStarted, List<FileBackup> backups)
        {
            if (installMoveStarted)
            {
                try
                {
                    if (Storage.FileExists(targetPath))
                    {
                        Storage.DeleteFile(targetPath);
                    }
                }
                catch (Exception ex)
                {
                    Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusTransactionalFileReplacement", "CleanIncompleteModUpdateFileFailed"), ex.Message));
                }
            }

            for (int i = backups.Count - 1; i >= 0; i--)
            {
                FileBackup backup = backups[i];
                try
                {
                    if (Storage.FileExists(backup.BackupPath))
                    {
                        Storage.MoveFile(backup.BackupPath, backup.OriginalPath);
                    }
                }
                catch (Exception ex)
                {
                    Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusTransactionalFileReplacement", "RestoreModUpdateBackupFailed"), ex.Message));
                }
            }
        }

        private sealed class FileBackup
        {
            public FileBackup(string originalPath, string backupPath)
            {
                OriginalPath = originalPath;
                BackupPath = backupPath;
            }

            public string OriginalPath { get; }

            public string BackupPath { get; }
        }
    }
}
