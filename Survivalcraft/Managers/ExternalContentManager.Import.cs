using System;
using System.IO;
using System.Threading.Tasks;
using Engine;

namespace Game
{
    // 外部内容导入统一入口：按准备结果分流的决策树导入 + 带备份回滚的低层导入。
    public static partial class ExternalContentManager
    {
        /// <summary>
        /// 按 PrepareExternalContent 产出的准备结果导入：
        /// 自家模组自更新 → 覆盖更新模组 → 覆盖文件型内容（备份回滚）→ 普通导入。
        /// </summary>
        public static string ImportPreparedExternalContent(
            string selfPackageName,
            Stream stream,
            ExternalContentPreparation result,
            bool overwriteExisting)
        {
            if (result == null)
            {
                throw new InvalidOperationException(LanguageControl.Get("ScbbsPlusRemoteImportManager", "ImportDataIsEmpty"));
            }

            if (CommunityFileTypes.IsModLike(result.Type))
            {
                if (string.Equals(result.ExistingModPackageName, selfPackageName, StringComparison.OrdinalIgnoreCase))
                {
                    return ModUpdateImporter.ImportSelfUpdate(stream, result.FileName, selfPackageName, result.ExistingModPath);
                }

                if (overwriteExisting && !string.IsNullOrWhiteSpace(result.ExistingModPackageName))
                {
                    return ModUpdateImporter.ImportModUpdate(
                        stream,
                        result.FileName,
                        result.ExistingModPackageName,
                        result.ExistingModPath);
                }
            }

            // 内置物不可覆盖，按条目名判定（$ 前缀/空名）；队列导入的持久化项只带路径
            // 不带名字，此时跳过判定——与收敛前队列直连导入器的行为一致。
            if (overwriteExisting && !string.IsNullOrWhiteSpace(result.ExistingEntryName))
            {
                EnsureExternalContentCanBeOverwritten(result.Type, result.ExistingEntryName);
            }

            // 覆盖导入需保证"导入失败不丢用户原内容"。删不可逆，故按类型分策：
            // 文件型（家具包/材质/皮肤）导入会原地写目标 → 交带备份回滚的导入器，先备份再覆盖、失败还原。
            // World 不参与覆盖（见 FindExistingExternalContentName 注释）：游戏原生导入总建新目录
            // （World/World1/…自动编号），重复导入即天然副本，永远走普通导入。
            if (overwriteExisting && !string.IsNullOrWhiteSpace(result.ExistingEntryFilePath))
            {
                return ImportExternalContentWithRollback(stream, result.Type, result.FileName, result.ExistingEntryFilePath);
            }

            // 原始字节透传：密文落盘、密文安装，引擎加载期自解密（ModsManager.GetScmods）。
            return ImportExternalContentSync(stream, result.Type, result.FileName);
        }

        /// <summary>
        /// 异步壳：后台线程执行 ImportPreparedExternalContent，成功/失败经回调返回
        ///（镜像 ImportExternalContent 的异步模式）。导入只消费 preparation.FileData。
        /// </summary>
        public static void ImportPreparedExternalContentAsync(
            string selfPackageName,
            ExternalContentPreparation preparation,
            bool overwriteExisting,
            Action<string> success,
            Action<Exception> failure)
        {
            Task.Run(
                delegate {
                    try {
                        if (preparation?.FileData == null) {
                            throw new InvalidOperationException(LanguageControl.Get("ScbbsPlusRemoteImportManager", "ImportDataIsEmpty"));
                        }
                        using MemoryStream stream = new(preparation.FileData, writable: false);
                        success?.Invoke(ImportPreparedExternalContent(selfPackageName, stream, preparation, overwriteExisting));
                    }
                    catch (Exception obj) {
                        failure?.Invoke(obj);
                    }
                }
            );
        }

        /// <summary>
        /// 覆盖导入：目标存在时先备份旧文件再导入，失败还原；无覆盖目标则直接导入。
        /// 流可能不可 seek（如下载流）：不可 seek 时先缓冲到内存再导入，
        /// 避免 Position=0 抛 NotSupportedException。
        /// </summary>
        public static string ImportExternalContentWithRollback(
            Stream stream,
            ExternalContentType type,
            string fileName,
            string overwriteExistingFilePath)
        {
            if (string.IsNullOrWhiteSpace(overwriteExistingFilePath)
                || !Storage.FileExists(overwriteExistingFilePath))
            {
                return ImportExternalContentSync(stream, type, fileName);
            }

            return TransactionalFileReplacement.OverwriteWithRollback(overwriteExistingFilePath, () =>
            {
                if (stream.CanSeek)
                {
                    stream.Position = 0;
                    return ImportExternalContentSync(stream, type, fileName);
                }
                using MemoryStream buffered = new();
                stream.CopyTo(buffered);
                buffered.Position = 0;
                return ImportExternalContentSync(buffered, type, fileName);
            });
        }
    }
}
