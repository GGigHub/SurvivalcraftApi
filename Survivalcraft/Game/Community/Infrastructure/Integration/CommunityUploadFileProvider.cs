using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Engine;

namespace Game
{
    internal sealed class CommunityUploadFileProvider : ICommunityUploadFileProvider
    {

        public CommunityPreparedUploadFile PrepareWorld(string directoryName, string worldName)
        {
            string tempPath = Storage.CombinePaths(ModsManager.DocPath, $"ScbbsPlusWorldUpload-{Guid.NewGuid():N}.tmp");
            try
            {
                using (Stream targetStream = Storage.OpenFile(tempPath, OpenFileMode.Create))
                {
                    WorldsManager.ExportWorld(directoryName, targetStream);
                }
                return new CommunityPreparedUploadFile
                {
                    FilePath = tempPath,
                    FileName = CommunityUploadFileNaming.MakeWorldFileName(worldName),
                    FileSize = Storage.GetFileSize(tempPath),
                    FileType = CommunityFileType.World,
                    IsTemporary = true
                };
            }
            catch
            {
                // 导出失败：清理已创建的临时文件再抛，避免每次失败泄漏一个 .tmp。
                TryDeleteTempFile(tempPath);
                throw;
            }
        }

        public CommunityPreparedUploadFile PrepareLocalResource(LocalResourceItem resourceItem)
        {
            if (resourceItem == null)
            {
                throw new InvalidOperationException(LanguageControl.Get("ScbbsPlusGameUploadFileProvider", "NoLocalResourceSelected"));
            }

            CommunityFileType fileType = GetUploadFileType(resourceItem);
            switch (resourceItem.Type)
            {
                case ExternalContentType.FurniturePack:
                {
                    string tempPath = Storage.CombinePaths(ModsManager.DocPath, $"ScbbsPlusFurnitureUpload-{Guid.NewGuid():N}.scfpack");
                    try
                    {
                        using (Stream targetStream = Storage.OpenFile(tempPath, OpenFileMode.Create))
                        {
                            FurniturePacksManager.ExportFurniturePack(resourceItem.Name, targetStream);
                        }
                        return new CommunityPreparedUploadFile
                        {
                            FilePath = tempPath,
                            FileName = CommunityUploadFileNaming.MakeSafeFileName(resourceItem.DisplayName ?? resourceItem.Name, ".scfpack"),
                            FileSize = Storage.GetFileSize(tempPath),
                            FileType = fileType,
                            IsTemporary = true
                        };
                    }
                    catch
                    {
                        // 导出失败：清理已创建的临时文件再抛，避免每次失败泄漏一个临时文件。
                        TryDeleteTempFile(tempPath);
                        throw;
                    }
                }

                case ExternalContentType.BlocksTexture:
                    return CreateExistingFileUpload(resourceItem, BlocksTexturesManager.GetFileName(resourceItem.Name), fileType, ".scbtex");

                case ExternalContentType.CharacterSkin:
                    return CreateExistingFileUpload(resourceItem, CharacterSkinsManager.GetFileName(resourceItem.Name), fileType, ".scskin");

                case ExternalContentType.Mod:
                    // 未加载（pending）模组没有 ModEntity，回退到记录中的本地路径，与 GetLocalResourceFilePath 保持一致。
                    return CreateExistingFileUpload(
                        resourceItem,
                        resourceItem.ModEntity?.ModFilePath ?? resourceItem.LocalFilePath,
                        fileType,
                        ".scmod");

                default:
                    throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusGameUploadFileProvider", "UnsupportedUploadResourceTypeFormat"), resourceItem.Type));
            }
        }

        // 缺失文件由 OpenFile(FileMode.Open) 抛 FileNotFoundException，无需预检查。
        public Stream OpenRead(string filePath)
        {
            return Storage.OpenFile(filePath, OpenFileMode.Read);
        }

        public void DeleteTemporaryFile(CommunityPreparedUploadFile uploadFile)
        {
            if (uploadFile?.IsTemporary == true && Storage.FileExists(uploadFile.FilePath))
            {
                Storage.DeleteFile(uploadFile.FilePath);
            }
        }

        public long? TryGetExistingFileSize(string filePath)
        {
            return Storage.TryGetFileSize(filePath, out long size) ? size : null;
        }

        public string GetLocalResourceFilePath(LocalResourceItem resourceItem)
        {
            if (resourceItem == null)
            {
                return null;
            }

            return resourceItem.Type switch
            {
                ExternalContentType.FurniturePack => FurniturePacksManager.GetFileName(resourceItem.Name),
                ExternalContentType.BlocksTexture => BlocksTexturesManager.GetFileName(resourceItem.Name),
                ExternalContentType.CharacterSkin => CharacterSkinsManager.GetFileName(resourceItem.Name),
                ExternalContentType.Mod => resourceItem.ModEntity?.ModFilePath ?? resourceItem.LocalFilePath,
                _ when IsModpack(resourceItem) => resourceItem.LocalFilePath,
                _ => null
            };
        }

        public static CommunityFileType GetUploadFileType(LocalResourceItem resourceItem)
            => CommunityFileTypes.FromExternal(GetUploadExternalType(resourceItem));

        /// <summary>域内上传类型：本地资源类型，未分类时按整合包标记（Tag）判 Modpack。</summary>
        public static ExternalContentType GetUploadExternalType(LocalResourceItem resourceItem)
        {
            ExternalContentType type = resourceItem?.Type ?? ExternalContentType.Unknown;
            return type != ExternalContentType.Unknown
                ? type
                : IsModpack(resourceItem) ? ExternalContentType.Modpack : ExternalContentType.Unknown;
        }

        public static string GetUploadFileName(LocalResourceItem resourceItem, string filePath)
        {
            return GetUploadFileName(resourceItem, filePath, GetFallbackUploadExtension(GetUploadExternalType(resourceItem)));
        }

        private CommunityPreparedUploadFile CreateExistingFileUpload(
            LocalResourceItem resourceItem,
            string filePath,
            CommunityFileType fileType,
            string fallbackExtension)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new InvalidOperationException(LanguageControl.Get("ScbbsPlusGameUploadFileProvider", "CannotFindLocalResourcePath"));
            }
            if (!Storage.FileExists(filePath))
            {
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusGameUploadFileProvider", "LocalFileDoesNotExistFormat"), filePath));
            }

            return new CommunityPreparedUploadFile
            {
                FilePath = filePath,
                FileName = GetUploadFileName(resourceItem, filePath, fallbackExtension),
                FileSize = Storage.GetFileSize(filePath),
                FileType = fileType
            };
        }

        private static string GetUploadFileName(LocalResourceItem resourceItem, string filePath, string fallbackExtension)
        {
            string fileName = Storage.GetFileName(filePath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return CommunityUploadFileNaming.MakeSafeFileName(
                    resourceItem?.DisplayName ?? resourceItem?.Name ?? "resource",
                    fallbackExtension);
            }

            if (string.IsNullOrWhiteSpace(Storage.GetExtensionBCL(fileName)))
            {
                fileName += fallbackExtension;
            }
            return CommunityUploadFileNaming.SanitizeFileName(fileName);
        }

        private static string GetFallbackUploadExtension(ExternalContentType type)
        {
            if (type == ExternalContentType.Modpack)
            {
                return ".json";
            }

            return type != ExternalContentType.Unknown
                ? ExternalContentManager.GetEntryTypeExtensions(type).FirstOrDefault() ?? ".dat"
                : ".dat";
        }

        private static void TryDeleteTempFile(string path)
        {
            try
            {
                Storage.DeleteFileIfExists(path);
            }
            catch (Exception ex)
            {
                Log.Warning(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusGameUploadFileProvider", "CleanUploadTempFileFailed"), path, ex.Message));
            }
        }

        private static bool IsModpack(LocalResourceItem resourceItem)
        {
            return resourceItem?.Tag is LocalModpackRecord;
        }
    }
}
