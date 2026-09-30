using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Engine;

namespace Game
{
    // 外部内容导入准备：识别类型、检测本地冲突，产出 ExternalContentPreparation 供 UI 确认与导入。
    public static partial class ExternalContentManager
    {
        /// <summary>
        /// 便捷重载：任意流（可为 provider/下载文件流）缓冲为内存流后走统一准备。
        /// 已是 MemoryStream 则零拷贝接管（调用方此后不得再读写原流）；否则整体
        /// 缓冲后立即释放原流。导入阶段只消费 preparation.FileData。
        /// </summary>
        public static ExternalContentPreparation PrepareExternalContent(
            int requestId,
            string fileName,
            ExternalContentType type,
            Stream stream)
        {
            MemoryStream buffer = stream as MemoryStream;
            if (buffer != null)
            {
                buffer.Position = 0;
            }
            else
            {
                buffer = new MemoryStream();
                try
                {
                    stream.CopyTo(buffer);
                }
                finally
                {
                    stream.Dispose();
                }
            }
            return PrepareExternalContent(requestId, new RemoteImportRequest
            {
                FileName = fileName,
                Type = type
            }, buffer);
        }

        public static ExternalContentPreparation PrepareExternalContent(
            int requestId,
            RemoteImportRequest request,
            MemoryStream stream)
        {
            // 精确容量 MemoryStream（CommunityHttpDownloader 按 Content-Length 预分配、调用方按包数组
            // 构造）TryGetBuffer 零拷贝接管底层缓冲；缓冲超配或短读时回退 ToArray 拷贝。
            byte[] fileData = stream.TryGetBuffer(out ArraySegment<byte> buffer)
                && buffer.Offset == 0
                && buffer.Count == buffer.Array.Length
                ? buffer.Array
                : stream.ToArray();
            ExternalContentPreparation result = new()
            {
                RequestId = requestId,
                Url = request.Url,
                FileName = request.FileName,
                Type = request.Type,
                FileData = fileData,
                DisplayName = Storage.GetFileNameWithoutExtensionBCL(request.FileName)
            };

            using MemoryStream analysisStream = new(fileData, writable: false);
            if (CommunityFileTypes.IsModLike(request.Type))
            {
                ModInfo modInfo = ScmodArchiveReader.ReadModInfo(analysisStream);
                result.DisplayName = string.IsNullOrWhiteSpace(modInfo?.Name)
                    ? Storage.GetFileNameWithoutExtensionBCL(request.FileName)
                    : modInfo.Name;
                ModEntity existingMod = FindInstalledMod(modInfo?.PackageName, request.FileName);
                if (existingMod?.modInfo != null)
                {
                    result.ExistingDisplayName = string.IsNullOrWhiteSpace(existingMod.modInfo.Name)
                        ? existingMod.modInfo.PackageName
                        : existingMod.modInfo.Name;
                    result.ExistingModPackageName = existingMod.modInfo.PackageName;
                    result.ExistingModPath = existingMod.ModFilePath;
                }
                // 模组一律重启后才加载（引擎启动期扫描 Mods 目录）：新装/覆盖都提示重启。
                result.RequiresRestart = true;
                return result;
            }

            string importName = Storage.GetFileNameWithoutExtensionBCL(request.FileName);
            string existingName = FindExistingExternalContentName(request.Type, importName);
            if (!string.IsNullOrWhiteSpace(existingName))
            {
                result.ExistingEntryName = existingName;
                result.ExistingDisplayName = existingName;
                result.ExistingEntryFilePath = GetExistingExternalContentFilePath(request.Type, existingName);
            }
            return result;
        }

        public static string GetContentTypeDisplayName(ExternalContentType type)
        {
            return LanguageControl.GetEnumDisplayName(type, LanguageControl.GetEnumDisplayName(ExternalContentType.Unknown));
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

        // 这些查找均在 PrepareExternalContent 中被调用，而本方法经由远程导入预备（Task.Run）与整合包导入器
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
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("RemoteImportManager", "CannotOverwriteBuiltInTexture"), existingName));
            }
            if (type == ExternalContentType.CharacterSkin && CharacterSkinsManager.IsBuiltIn(existingName))
            {
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("RemoteImportManager", "CannotOverwriteBuiltInSkin"), existingName));
            }
        }

        /// <summary>
        /// 按包名（其次磁盘文件名）在已加载模组中定位实体。枚举 ModListAll 封送主线程；
        /// 主线程调用时 Dispatch 内联执行，无额外开销（ExternalContentScreen 导入入队即此用法）。
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

        private static string NormalizeName(string name) => (name ?? string.Empty).Trim();

        private static bool NamesMatch(string left, string right)
        {
            return !string.IsNullOrWhiteSpace(left)
                && !string.IsNullOrWhiteSpace(right)
                && string.Equals(NormalizeName(left), NormalizeName(right), StringComparison.OrdinalIgnoreCase);
        }
    }
}
