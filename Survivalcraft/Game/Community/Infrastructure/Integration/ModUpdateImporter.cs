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
            ModInfo updateInfo = ScmodArchiveReader.ReadModInfo(stream);
            if (updateInfo == null || !string.Equals(updateInfo.PackageName, packageName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(LanguageControl.Get("ScbbsPlusCommunityScreen", "Upload", "Update", "UpdatePackageIsNotScbbsPlusMod"));
            }

            string targetPath = Storage.CombinePaths(ModsManager.ModsPath, GetSafeFileName(fileName, "ScbbsPlus.scmod"));
            return ReplaceModFile(stream, targetPath, existingModFilePath, "ScbbsPlus-update");
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

            if (!TryReadModInfo(stream, out ModInfo updateInfo) || updateInfo == null)
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
            return ReplaceModFile(stream, targetPath, oldModFilePath, "ScbbsPlus-mod-update");
        }

        // 原始字节直通：探测走读器（内存解密、复位流位置），落盘 CopyTo 流式写，
        // 不再整包驻留 byte[]——大包导入期内存峰值从"数组+读器缓冲"两三份降到 80KB 缓冲。
        private static string ReplaceModFile(Stream data, string targetPath, string existingPath, string tempFilePrefix)
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
                    data.CopyTo(fileStream);
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

        private static bool TryReadModInfo(Stream stream, out ModInfo modInfo)
        {
            try
            {
                return ScmodArchiveReader.TryReadModInfo(stream, out modInfo);
            }
            catch (InvalidScmodArchiveException)
            {
                modInfo = null;
                return false;
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
