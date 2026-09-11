using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Engine;

namespace Game
{
    /// <summary>
    /// 识别远程内容、检查本地冲突，并执行已准备内容的导入。
    /// </summary>
    internal static class RemoteImportContent
    {
        public static RemoteImportRequest CreateRequest(string rawUrl, string preferredFileName = null)
        {
            string url = (rawUrl ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new InvalidOperationException(LanguageControl.Get("ScbbsPlusRemoteImportManager", "EnterResourceLink"));
            }
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException(LanguageControl.Get("ScbbsPlusRemoteImportManager", "InvalidUrl"));
            }

            // 先解码再取文件名：%2F%2E%2E 等转义必须先展开，否则 ".." 段会随文件名
            // 一路带入引擎 ImportMod 的 CombinePaths，构成远程链接任意路径写入。
            string fileName = string.IsNullOrWhiteSpace(preferredFileName)
                ? Storage.GetFileName(Uri.UnescapeDataString(uri.AbsolutePath ?? string.Empty))
                : Storage.GetFileName(preferredFileName.Trim());
            if (string.IsNullOrWhiteSpace(fileName)
                || fileName.IndexOf('/') >= 0
                || fileName.IndexOf('\\') >= 0
                || fileName == "."
                || fileName == "..")
            {
                throw new InvalidOperationException(LanguageControl.Get("ScbbsPlusRemoteImportManager", "CannotRecognizeFileName"));
            }

            ExternalContentType type = ResolveType(fileName);
            if (type == ExternalContentType.Unknown)
            {
                throw new InvalidOperationException(LanguageControl.Get("ScbbsPlusRemoteImportManager", "UnsupportedFileExtension"));
            }

            return new RemoteImportRequest
            {
                Url = uri.ToString(),
                FileName = fileName,
                Type = type
            };
        }

        public static RemoteImportPreparation Analyze(
            string selfPackageName,
            int requestId,
            RemoteImportRequest request,
            MemoryStream stream)
        {
            byte[] fileData = stream.ToArray();
            RemoteImportPreparation result = new()
            {
                RequestId = requestId,
                Url = request.Url,
                FileName = request.FileName,
                Type = request.Type,
                FileData = fileData,
                DisplayName = FileNameRules.GetFileNameWithoutExtension(request.FileName)
            };

            using MemoryStream analysisStream = new(fileData, writable: false);
            if (request.Type == ExternalContentType.Mod)
            {
                ModInfo modInfo = ScmodArchive.ReadModInfoFromScmodData(analysisStream);
                result.DisplayName = string.IsNullOrWhiteSpace(modInfo?.Name)
                    ? FileNameRules.GetFileNameWithoutExtension(request.FileName)
                    : modInfo.Name;
                ModEntity existingMod = FindInstalledMod(modInfo?.PackageName, request.FileName);
                if (existingMod?.modInfo != null)
                {
                    result.ExistingDisplayName = string.IsNullOrWhiteSpace(existingMod.modInfo.Name)
                        ? existingMod.modInfo.PackageName
                        : existingMod.modInfo.Name;
                    result.ExistingModPackageName = existingMod.modInfo.PackageName;
                    result.ExistingModPath = existingMod.ModFilePath;
                    result.RequiresRestart = string.Equals(
                        existingMod.modInfo.PackageName,
                        selfPackageName,
                        StringComparison.OrdinalIgnoreCase);
                }
                return result;
            }

            string importName = FileNameRules.GetFileNameWithoutExtension(request.FileName);
            string existingName = FindExistingExternalContentName(request.Type, importName);
            if (!string.IsNullOrWhiteSpace(existingName))
            {
                result.ExistingEntryName = existingName;
                result.ExistingDisplayName = existingName;
            }
            return result;
        }

        public static string Import(
            string selfPackageName,
            Stream stream,
            RemoteImportPreparation result,
            bool overwriteExisting)
        {
            if (result == null)
            {
                throw new InvalidOperationException(LanguageControl.Get("ScbbsPlusRemoteImportManager", "ImportDataIsEmpty"));
            }

            if (result.Type == ExternalContentType.Mod)
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

                return ScmodArchive.ImportExternalContentSyncNormalized(stream, result.Type, result.FileName);
            }

            if (overwriteExisting && !string.IsNullOrWhiteSpace(result.ExistingEntryName))
            {
                return ImportWithOverwriteRollback(stream, result.Type, result.ExistingEntryName, result.FileName);
            }

            return ScmodArchive.ImportExternalContentSyncNormalized(stream, result.Type, result.FileName);
        }

        // 覆盖导入需保证"导入失败不丢用户原内容"。删不可逆，故按类型分策：
        // 文件型（家具包/材质/皮肤）导入会原地写目标 → 复用带备份回滚的导入器，先备份再覆盖、失败还原。
        // World 不参与覆盖（见 FindExistingExternalContentName 注释）：游戏原生导入总建新目录
        // （World/World1/…自动编号），重复导入即天然副本，永远走普通导入。
        private static string ImportWithOverwriteRollback(
            Stream stream,
            ExternalContentType type,
            string existingName,
            string importFileName)
        {
            EnsureExternalContentCanBeOverwritten(type, existingName);

            string existingPath = GetExistingExternalContentFilePath(type, existingName);
            return ExternalContentImporter.Import(stream, type, importFileName, existingPath);
        }

        private static string GetExistingExternalContentFilePath(ExternalContentType type, string existingName)
        {
            return type switch
            {
                ExternalContentType.FurniturePack => FurniturePacksManager.GetFileName(existingName),
                ExternalContentType.BlocksTexture => BlocksTexturesManager.GetFileName(existingName),
                ExternalContentType.CharacterSkin => CharacterSkinsManager.GetFileName(existingName),
                _ => null,
            };
        }

        public static string GetContentTypeDisplayName(ExternalContentType type)
        {
            return EnumDisplayName.GetEnumDisplayName(type, EnumDisplayName.GetEnumDisplayName(ScbbsPlusCommunityFileType.Unknown));
        }

        internal static ExternalContentType ResolveType(string fileName)
        {
            ExternalContentType type = ExternalContentManager.ExtensionToType(
                FileNameRules.GetExtension(fileName ?? string.Empty));
            return ExternalContentManager.IsEntryTypeDownloadSupported(type)
                ? type
                : ExternalContentType.Unknown;
        }

        /// <summary>
        /// 导入覆盖前判同名内容的唯一权威口径（缺陷 1 补审 F1）：家具/材质/皮肤同名归一匹配。
        /// World 不参与判定——游戏原生导入总建新目录（World/World1/…自动编号，目录名与世界名
        /// 无关），重复导入即天然副本，无需覆盖/重命名流程。整合包跳过判定必须复用本函数，
        /// 与 InstallStatusService.FindExactLocalDownloadFilePath 的文件名精确匹配互补。
        /// </summary>
        internal static string FindExistingExternalContentName(ExternalContentType type, string candidateName)
        {
            string normalized = NormalizeName(candidateName);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return null;
            }

            return type switch
            {
                ExternalContentType.FurniturePack => FindExistingFurniturePackName(normalized),
                ExternalContentType.BlocksTexture => FindExistingBlocksTextureName(normalized),
                ExternalContentType.CharacterSkin => FindExistingCharacterSkinName(normalized),
                _ => null
            };
        }

        // 这些查找均在 Analyze 中被调用，而 Analyze 经由远程导入预备（Task.Run）与整合包导入器
        // （工作流后台 foreach）运行在后台线程。游戏的 Update*List() 对其内部 List 做 Clear()+Add()，
        // 主线程渲染/界面可能并发枚举同一列表 → InvalidOperationException 或脏读。
        // 故把 Update*List() 与枚举整体封送到主线程同步执行（Dispatch 内联于主线程、阻塞后台线程
        // 至下一帧）。主线程从不同步等待这些后台任务（均先查 IsCompleted 再读结果），无死锁风险。
        private static string FindExistingFurniturePackName(string normalizedName)
        {
            string found = null;
            Dispatcher.Dispatch(() =>
            {
                FurniturePacksManager.UpdateFurniturePacksList();
                foreach (string name in FurniturePacksManager.FurniturePackNames)
                {
                    if (NamesMatch(name, normalizedName))
                    {
                        found = name;
                        break;
                    }
                }
            }, waitUntilCompleted: true);
            return found;
        }

        private static string FindExistingBlocksTextureName(string normalizedName)
        {
            string found = null;
            Dispatcher.Dispatch(() =>
            {
                BlocksTexturesManager.UpdateBlocksTexturesList();
                foreach (string name in BlocksTexturesManager.BlockTexturesNames)
                {
                    if (NamesMatch(name, normalizedName))
                    {
                        found = name;
                        break;
                    }
                }
            }, waitUntilCompleted: true);
            return found;
        }

        private static string FindExistingCharacterSkinName(string normalizedName)
        {
            string found = null;
            Dispatcher.Dispatch(() =>
            {
                CharacterSkinsManager.UpdateCharacterSkinsList();
                foreach (string name in CharacterSkinsManager.CharacterSkinsNames)
                {
                    if (NamesMatch(name, normalizedName))
                    {
                        found = name;
                        break;
                    }
                }
            }, waitUntilCompleted: true);
            return found;
        }

        private static void EnsureExternalContentCanBeOverwritten(ExternalContentType type, string existingName)
        {
            if (type == ExternalContentType.BlocksTexture && BlocksTexturesManager.IsBuiltIn(existingName))
            {
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusRemoteImportManager", "CannotOverwriteBuiltInTexture"), existingName));
            }
            if (type == ExternalContentType.CharacterSkin && CharacterSkinsManager.IsBuiltIn(existingName))
            {
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusRemoteImportManager", "CannotOverwriteBuiltInSkin"), existingName));
            }
        }

        /// <summary>
        /// 按包名（其次磁盘文件名）在已加载模组中定位实体。枚举 ModListAll 封送主线程；
        /// 主线程调用时 Dispatch 内联执行，无额外开销（MoreScreen 自更新入队即此用法）。
        /// </summary>
        internal static ModEntity FindInstalledMod(string packageName, string fileName)
        {
            string safeFileName = Storage.GetFileName(fileName ?? string.Empty);
            ModEntity found = null;
            // ModListAll 同为游戏全局 List，排序/加载时 Clear()+AddRange()。后台枚举与主线程并发，
            // 故同样封主线程同步枚举（参见上方 FindExisting*Name 的封送说明）。
            Dispatcher.Dispatch(() =>
            {
                foreach (ModEntity entity in ModsManager.ModListAll)
                {
                    if (entity?.modInfo == null)
                    {
                        continue;
                    }
                    if (!string.IsNullOrWhiteSpace(packageName)
                        && string.Equals(entity.modInfo.PackageName, packageName, StringComparison.OrdinalIgnoreCase))
                    {
                        found = entity;
                        break;
                    }
                    if (!string.IsNullOrWhiteSpace(safeFileName)
                        && !string.IsNullOrWhiteSpace(entity.ModFilePath)
                        && string.Equals(Storage.GetFileName(entity.ModFilePath), safeFileName, StringComparison.OrdinalIgnoreCase))
                    {
                        found = entity;
                        break;
                    }
                }
            }, waitUntilCompleted: true);
            return found;
        }

        private static string NormalizeName(string name) => (name ?? string.Empty).Trim();

        private static bool NamesMatch(string left, string right)
        {
            return !string.IsNullOrWhiteSpace(left)
                && !string.IsNullOrWhiteSpace(right)
                && string.Equals(NormalizeName(left), NormalizeName(right), StringComparison.OrdinalIgnoreCase);
        }
    }

    internal sealed class RemoteImportRequest
    {
        public string Url { get; set; }
        public string FileName { get; set; }
        public ExternalContentType Type { get; set; }
    }

    internal sealed class RemoteImportPreparation
    {
        public int RequestId { get; set; }
        public string Url { get; set; }
        public string FileName { get; set; }
        public ExternalContentType Type { get; set; }
        public byte[] FileData { get; set; }
        public string DisplayName { get; set; }
        public string ExistingEntryName { get; set; }
        public string ExistingDisplayName { get; set; }
        public string ExistingModPackageName { get; set; }
        public string ExistingModPath { get; set; }
        public bool RequiresRestart { get; set; }
        public string Error { get; set; }
        public bool ArchiveSaved { get; set; }
        public ModpackManifest Manifest { get; set; }
        public IReadOnlyDictionary<string, byte[]> EmbeddedFiles { get; set; }
        public byte[] OriginalData { get; set; }

        public bool RequiresOverwriteConfirmation =>
            !string.IsNullOrWhiteSpace(ExistingEntryName)
            || !string.IsNullOrWhiteSpace(ExistingModPackageName);
    }
}
