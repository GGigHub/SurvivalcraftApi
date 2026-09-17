using System.IO;
using Engine;

namespace Game
{
    /// <summary>
    /// 把下载流导入 Survivalcraft 外部内容，覆盖时委托事务替换备份旧文件。
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

            return TransactionalFileReplacement.OverwriteWithRollback(overwriteExistingFilePath, () =>
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
    }
}
