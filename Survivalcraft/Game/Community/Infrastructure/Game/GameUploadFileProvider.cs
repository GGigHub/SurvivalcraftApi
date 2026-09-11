using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Engine;

namespace Game
{
    internal sealed class GameUploadFileProvider : IUploadFileProvider
    {

        public PreparedUploadFile PrepareWorld(string directoryName, string worldName)
        {
            string tempPath = Storage.CombinePaths(ModsManager.DocPath, $"ScbbsPlusWorldUpload-{Guid.NewGuid():N}.tmp");
            try
            {
                using (Stream targetStream = Storage.OpenFile(tempPath, OpenFileMode.Create))
                {
                    WorldsManager.ExportWorld(directoryName, targetStream);
                }
                return new PreparedUploadFile
                {
                    FilePath = tempPath,
                    FileName = UploadFileNaming.MakeWorldFileName(worldName),
                    FileSize = GetExistingFileSize(tempPath),
                    FileType = ScbbsPlusCommunityFileType.World,
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

        public PreparedUploadFile PrepareLocalResource(LocalResourceItem resourceItem)
        {
            if (resourceItem == null)
            {
                throw new InvalidOperationException(LanguageControl.Get("ScbbsPlusCommunityScreen", "Upload", "WorldUpload", "NoLocalResourceSelected"));
            }

            ScbbsPlusCommunityFileType fileType = GetUploadFileType(resourceItem);
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
                        return new PreparedUploadFile
                        {
                            FilePath = tempPath,
                            FileName = UploadFileNaming.MakeSafeFileName(resourceItem.DisplayName ?? resourceItem.Name, ".scfpack"),
                            FileSize = GetExistingFileSize(tempPath),
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
                    throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusCommunityScreen", "Upload", "WorldUpload", "UnsupportedUploadResourceTypeFormat"), resourceItem.Type));
            }
        }

        // 缺失文件由 OpenFile(FileMode.Open) 抛 FileNotFoundException，无需预检查。
        public Stream OpenRead(string filePath)
        {
            return Storage.OpenFile(filePath, OpenFileMode.Read);
        }

        public void DeleteTemporaryFile(PreparedUploadFile uploadFile)
        {
            if (uploadFile?.IsTemporary == true && Storage.FileExists(uploadFile.FilePath))
            {
                Storage.DeleteFile(uploadFile.FilePath);
            }
        }

        private long GetExistingFileSize(string filePath)
        {
            using Stream stream = OpenRead(filePath);
            return stream.Length;
        }

        public long? TryGetExistingFileSize(string filePath)
        {
            try
            {
                return GetExistingFileSize(filePath);
            }
            catch
            {
                return null;
            }
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

        public static string GetUploadExtensionText(string filePath, ScbbsPlusCommunityFileType fileType)
        {
            string extension = FileNameRules.GetExtension(filePath);
            if (!string.IsNullOrWhiteSpace(extension))
            {
                return extension.TrimStart('.').ToLowerInvariant();
            }

            return fileType switch
            {
                ScbbsPlusCommunityFileType.World => "scworld",
                ScbbsPlusCommunityFileType.Furniture => "scfpack",
                ScbbsPlusCommunityFileType.Texture => "png/scbtex",
                ScbbsPlusCommunityFileType.Skin => "scskin",
                ScbbsPlusCommunityFileType.Mod => "scmod",
                ScbbsPlusCommunityFileType.Modpack => "zip/json",
                _ => "-"
            };
        }

        public static ScbbsPlusCommunityFileType GetUploadFileType(LocalResourceItem resourceItem)
        {
            ScbbsPlusCommunityFileType fileType = CommunityFileRules.MapExternalContentTypeToFileType(resourceItem?.Type ?? ExternalContentType.Unknown);
            return fileType != ScbbsPlusCommunityFileType.Unknown
                ? fileType
                : IsModpack(resourceItem) ? CommunityFileTypes.ModpackFileType : ScbbsPlusCommunityFileType.Unknown;
        }

        public static string GetUploadFileName(LocalResourceItem resourceItem, string filePath)
        {
            return GetUploadFileName(resourceItem, filePath, GetFallbackUploadExtension(GetUploadFileType(resourceItem)));
        }

        private PreparedUploadFile CreateExistingFileUpload(
            LocalResourceItem resourceItem,
            string filePath,
            ScbbsPlusCommunityFileType fileType,
            string fallbackExtension)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new InvalidOperationException(LanguageControl.Get("ScbbsPlusCommunityScreen", "Upload", "WorldUpload", "CannotFindLocalResourcePath"));
            }
            if (!Storage.FileExists(filePath))
            {
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusCommunityScreen", "Upload", "WorldUpload", "LocalFileDoesNotExistFormat"), filePath));
            }

            return new PreparedUploadFile
            {
                FilePath = filePath,
                FileName = GetUploadFileName(resourceItem, filePath, fallbackExtension),
                FileSize = GetExistingFileSize(filePath),
                FileType = fileType
            };
        }

        private static string GetUploadFileName(LocalResourceItem resourceItem, string filePath, string fallbackExtension)
        {
            string fileName = Storage.GetFileName(filePath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return UploadFileNaming.MakeSafeFileName(
                    resourceItem?.DisplayName ?? resourceItem?.Name ?? "resource",
                    fallbackExtension);
            }

            if (string.IsNullOrWhiteSpace(FileNameRules.GetExtension(fileName)))
            {
                fileName += fallbackExtension;
            }
            return UploadFileNaming.SanitizeFileName(fileName);
        }

        private static string GetFallbackUploadExtension(ScbbsPlusCommunityFileType fileType)
        {
            ExternalContentType type = CommunityFileRules.MapFileTypeToExternalContentType(fileType);
            if (type != ExternalContentType.Unknown)
            {
                return ExternalContentManager.GetEntryTypeExtensions(type).FirstOrDefault() ?? ".dat";
            }

            return fileType switch
            {
                ScbbsPlusCommunityFileType.Modpack => ".json",
                _ => ".dat"
            };
        }

        private static void TryDeleteTempFile(string path)
        {
            try
            {
                if (Storage.FileExists(path))
                {
                    Storage.DeleteFile(path);
                }
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
