using System;
using System.Globalization;
using System.IO;
using Engine;

namespace Game
{
    /// <summary>
    /// 安装已校验的模组更新包，并在替换失败时保持原文件可恢复。
    /// </summary>
    internal static class ModUpdateImporter
    {
        // 旧安装文件路径由调用方在分析/确认阶段查得（封送主线程的权威口径，见
        // RemoteImportContentService.FindInstalledMod），导入线程不再重扫 ModListAll：
        // 后台裸枚举与主线程列表重建并发，靠吞异常降级会漏删旧文件。
        public static string ImportSelfUpdate(Stream stream, string fileName, string packageName, string existingModFilePath = null)
        {
            byte[] data = ReadAllBytes(stream);
            ModInfo updateInfo = ScmodArchiveReader.ReadModInfo(new MemoryStream(data, writable: false));
            if (updateInfo == null || !string.Equals(updateInfo.PackageName, packageName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(LanguageControl.Get("ScbbsPlusCommunityScreen", "Upload", "Update", "UpdatePackageIsNotScbbsPlusMod"));
            }

            string targetPath = Storage.CombinePaths(ModsManager.ModsPath, GetSafeFileName(fileName, "ScbbsPlus.scmod"));
            return ReplaceModFile(data, targetPath, existingModFilePath, "ScbbsPlus-update");
        }

        public static string ImportModUpdate(
            Stream stream,
            string fileName,
            string expectedPackageName,
            string oldModFilePath)
        {
            if (string.IsNullOrWhiteSpace(expectedPackageName))
            {
                throw new InvalidOperationException(LanguageControl.Get("ScbbsPlusCommunityScreen", "Upload", "Update", "CannotConfirmCurrentPackageName"));
            }

            byte[] fileData = ReadAllBytes(stream);
            if (!TryReadModInfo(fileData, out ModInfo updateInfo) || updateInfo == null)
            {
                // 与 ImportSelfUpdate 的强校验一致：损坏包直接拒绝，避免用坏包静默替换用户原 mod。
                throw new InvalidOperationException(LanguageControl.Get("ScbbsPlusCommunityScreen", "Upload", "Update", "UpdatePackageInvalidOrCorrupted"));
            }
            if (!string.Equals(updateInfo.PackageName, expectedPackageName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusCommunityScreen", "Upload", "Update", "UpdatePackagePackageNameMismatch"), expectedPackageName));
            }

            string targetPath = Storage.CombinePaths(
                ModsManager.ModsPath,
                GetSafeFileName(fileName, $"{expectedPackageName}.scmod"));
            return ReplaceModFile(fileData, targetPath, oldModFilePath, "ScbbsPlus-mod-update");
        }

        private static string ReplaceModFile(byte[] data, string targetPath, string existingPath, string tempFilePrefix)
        {
            if (!Storage.DirectoryExists(ModsManager.ModsPath))
            {
                Storage.CreateDirectory(ModsManager.ModsPath);
            }

            string tempPath = Storage.CombinePaths(ModsManager.ModsPath, $"{tempFilePrefix}-{Guid.NewGuid():N}.tmp");
            try
            {
                using (Stream fileStream = Storage.OpenFile(tempPath, OpenFileMode.Create))
                {
                    fileStream.Write(data, 0, data.Length);
                }

                TransactionalFileReplacement.Replace(tempPath, targetPath, existingPath);
                return Storage.GetFileName(targetPath);
            }
            catch
            {
                TryDeleteFile(tempPath);
                throw;
            }
        }

        private static bool TryReadModInfo(byte[] data, out ModInfo modInfo)
        {
            try
            {
                return ScmodArchiveReader.TryReadModInfo(new MemoryStream(data, writable: false), out modInfo);
            }
            catch (InvalidScmodArchiveException)
            {
                modInfo = null;
                return false;
            }
        }

        private static byte[] ReadAllBytes(Stream stream)
        {
            if (!stream.CanSeek)
            {
                throw new InvalidOperationException(LanguageControl.Get("ScbbsPlusCommunityScreen", "Upload", "Update", "UpdatePackageDataUnreadable"));
            }

            long originalPosition = stream.Position;
            try
            {
                stream.Position = 0;
                using MemoryStream dataStream = new();
                stream.CopyTo(dataStream);
                return dataStream.ToArray();
            }
            finally
            {
                stream.Position = originalPosition;
            }
        }

        private static string GetSafeFileName(string fileName, string fallbackName)
        {
            // 段级清洗统一走 UploadFileNaming（原手写版不清洗非法字符、
            // 不处理保留名、不截断长度，脏文件名会在 OpenFile(Create) 时写失败）。
            // fallback 全名自带 .scmod 后缀，清洗后即通过补扩展名检查原样返回。
            return UploadFileNaming.MakeSafeFileName(
                Storage.GetFileName(fileName ?? string.Empty),
                ModsManager.ModSuffix,
                fallbackName);
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (Storage.FileExists(path))
                {
                    Storage.DeleteFile(path);
                }
            }
            catch
            {
                // 清理失败不应掩盖真正的导入错误。
            }
        }
    }
}
