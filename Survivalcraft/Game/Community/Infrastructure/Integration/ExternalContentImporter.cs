using System;
using System.Globalization;
using System.IO;
using Engine;

namespace Game
{
    /// <summary>
    /// 把下载流导入 Survivalcraft 外部内容，并处理覆盖时的旧文件备份。
    /// </summary>
    internal static class ExternalContentImporter
    {
        public static string Import(
            Stream stream,
            ExternalContentType type,
            string fileName,
            string overwriteExistingFilePath)
        {
            if (string.IsNullOrWhiteSpace(overwriteExistingFilePath)
                || !Storage.FileExists(overwriteExistingFilePath))
            {
                return ExternalContentManager.ImportExternalContentSync(stream, type, fileName);
            }

            return ReplaceFileWithRollback(overwriteExistingFilePath, () =>
            {
                // 流可能不可 seek（如下载流）：不可 seek 时先缓冲到内存再导入，
                // 避免 Position=0 抛 NotSupportedException。
                if (stream.CanSeek)
                {
                    stream.Position = 0;
                    return ExternalContentManager.ImportExternalContentSync(stream, type, fileName);
                }
                using MemoryStream buffered = new();
                stream.CopyTo(buffered);
                buffered.Position = 0;
                return ExternalContentManager.ImportExternalContentSync(buffered, type, fileName);
            });
        }

        // 备份既有文件后执行 import；成功则清理备份，失败则清掉半成品并还原备份，
        // 避免覆盖导入中途失败（损坏包/磁盘满/校验失败）造成用户原内容永久丢失。
        // import 以委托注入，便于对回滚机制做磁盘级单元测试（无需游戏外部内容运行时）。
        internal static string ReplaceFileWithRollback(string existingPath, Func<string> import)
        {
            string backupPath =
                $"{existingPath}{TransactionalFileReplacement.BackupMarker}{Guid.NewGuid():N}";
            if (Storage.FileExists(backupPath))
            {
                Storage.DeleteFile(backupPath);
            }

            Storage.MoveFile(existingPath, backupPath);
            try
            {
                string importedName = import();
                DeleteBackup(backupPath);
                return importedName;
            }
            catch
            {
                try
                {
                    // 导入可能已部分写入目标路径，还原前先清掉，确保备份能落回原位。
                    if (Storage.FileExists(existingPath))
                    {
                        Storage.DeleteFile(existingPath);
                    }
                    if (Storage.FileExists(backupPath))
                    {
                        Storage.MoveFile(backupPath, existingPath);
                    }
                }
                catch (Exception restoreEx)
                {
                    Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusExternalContentImporter", "RestoreReplacedFileFailed"), restoreEx.Message));
                }
                throw;
            }
        }

        private static void DeleteBackup(string backupPath)
        {
            try
            {
                if (Storage.FileExists(backupPath))
                {
                    Storage.DeleteFile(backupPath);
                }
            }
            catch (Exception ex)
            {
                Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusExternalContentImporter", "CleanReplacementBackupFailed"), ex.Message));
            }
        }
    }
}
