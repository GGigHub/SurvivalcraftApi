using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Engine;

namespace Game
{
    internal static class ModpackRules
    {
        public static void NormalizeManifest(ModpackManifest manifest)
        {
            if (manifest?.Items == null)
            {
                return;
            }

            foreach (ModpackItem item in manifest.Items)
            {
                if (item == null)
                {
                    continue;
                }

                item.HasExplicitFileName = !string.IsNullOrWhiteSpace(item.Filename);
                item.ManifestFileName = item.HasExplicitFileName
                    ? Storage.GetFileName(item.Filename.Trim())
                    : null;
                item.Filename = GetItemFileName(item);
                if (string.IsNullOrWhiteSpace(item.TargetDirectory) && !string.IsNullOrWhiteSpace(item.TargetPath))
                {
                    item.TargetDirectory = Storage.GetDirectoryName(item.TargetPath).Replace('\\', '/');
                    if (string.IsNullOrWhiteSpace(item.Filename))
                    {
                        item.Filename = Storage.GetFileName(item.TargetPath);
                    }
                }
            }
        }

        public static bool TryGetEmbeddedFile(
            IReadOnlyDictionary<string, byte[]> embeddedFiles,
            string relativePath,
            out byte[] data)
        {
            data = null;
            string key = NormalizeArchiveEntryPath(relativePath);
            return !string.IsNullOrWhiteSpace(key)
                && embeddedFiles != null
                && embeddedFiles.TryGetValue(key, out data);
        }

        public static bool IsHttpUrl(string url)
        {
            return Uri.TryCreate((url ?? string.Empty).Trim(), UriKind.Absolute, out Uri uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }

        public static string GetItemFileName(ModpackItem item)
        {
            if (item == null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(item.Filename))
            {
                return Storage.GetFileName(item.Filename.Trim());
            }

            string fileName = TryGetPathTailName(item.Url);
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                return Storage.GetFileName(fileName);
            }

            return string.IsNullOrWhiteSpace(item.TargetPath)
                ? null
                : Storage.GetFileName(item.TargetPath);
        }

        public static string TryGetPathTailName(string value)
        {
            string normalized = (value ?? string.Empty).Trim().Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return null;
            }

            if (Uri.TryCreate(normalized, UriKind.Absolute, out Uri uri))
            {
                // LocalPath 已做一次百分号解码，再 Unescape 会二次解码：%2520（字面 %20）被错解成空格。
                // 取未解码的 AbsolutePath 后统一解码一次。
                normalized = Uri.UnescapeDataString(uri.AbsolutePath ?? string.Empty).Replace('\\', '/');
            }

            string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (int i = segments.Length - 1; i >= 0; i--)
            {
                string segment = segments[i]?.Trim();
                if (!string.IsNullOrWhiteSpace(segment))
                {
                    return Storage.GetFileName(segment);
                }
            }

            return null;
        }

        public static ExternalContentType ResolveContentType(ModpackItem item)
        {
            string targetDirectory = item?.TargetDirectory?.Trim().Replace('\\', '/');
            if (!string.IsNullOrWhiteSpace(targetDirectory))
            {
                string root = targetDirectory.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                return (root ?? string.Empty).ToLowerInvariant() switch
                {
                    "mods" => ExternalContentType.Mod,
                    "worlds" => ExternalContentType.World,
                    "texturepacks" => ExternalContentType.BlocksTexture,
                    "characterskins" => ExternalContentType.CharacterSkin,
                    "furniturepacks" => ExternalContentType.FurniturePack,
                    _ => ExternalContentType.Unknown
                };
            }

            return ResolveCategory(item?.Category);
        }

        public static bool IsCustomDataFile(ModpackItem item)
        {
            return item != null
                && ResolveContentType(item) == ExternalContentType.Unknown
                && (!string.IsNullOrWhiteSpace(item.TargetDirectory)
                    || string.Equals(item.Category, "data", StringComparison.OrdinalIgnoreCase));
        }

        public static bool TryValidateSource(
            ModpackItem item,
            bool supportsEmbeddedFiles,
            IReadOnlyDictionary<string, byte[]> embeddedFiles,
            out string error,
            bool allowMissingEmbeddedFile = false)
        {
            error = null;
            if (item == null)
            {
                error = LanguageControl.Get("ScbbsPlusModpackRules", "PackItemIsEmpty");
                return false;
            }

            if (string.IsNullOrWhiteSpace(item.TargetDirectory))
            {
                error = string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusModpackRules", "PackItemMissingTargetDirectory"), GetDisplayName(item));
                return false;
            }

            string url = item.Url?.Trim();
            string fileId = item.FileId?.Trim();
            if (string.IsNullOrWhiteSpace(url) && string.IsNullOrWhiteSpace(fileId))
            {
                error = string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusModpackRules", "PackItemUrlAndFileIdBothEmpty"), GetDisplayName(item));
                return false;
            }

            if (!string.IsNullOrWhiteSpace(url) && !IsHttpUrl(url))
            {
                if (!supportsEmbeddedFiles)
                {
                    if (!string.IsNullOrWhiteSpace(fileId))
                    {
                        return true;
                    }
                    error = string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusModpackRules", "JsonPackNoLocalRelativePath"), GetDisplayName(item));
                    return false;
                }

                if (!TryGetEmbeddedFile(embeddedFiles, url, out _))
                {
                    if (!string.IsNullOrWhiteSpace(fileId) || allowMissingEmbeddedFile)
                    {
                        return true;
                    }
                    error = string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusModpackRules", "PackItemFileNotFoundInArchive"), GetDisplayName(item), url);
                    return false;
                }
            }

            return true;
        }

        public static string EnsureItemFileName(
            ModpackItem item,
            ExternalContentType type,
            PostFileItem communityFile = null,
            string downloadedFileName = null)
        {
            string fileName = item?.HasExplicitFileName == true
                ? Storage.GetFileName((item.ManifestFileName ?? item.Filename)?.Trim() ?? string.Empty)
                : null;
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = CommunityFileRules.GetDownloadFileName(communityFile);
            }
            fileName ??= downloadedFileName;
            fileName ??= TryGetPathTailName(item?.Url);
            if (string.IsNullOrWhiteSpace(fileName) && !string.IsNullOrWhiteSpace(item?.TargetPath))
            {
                fileName = Storage.GetFileName(item.TargetPath);
            }
            fileName = string.IsNullOrWhiteSpace(fileName) ? "download" : Storage.GetFileName(fileName);
            if (!string.IsNullOrWhiteSpace(Storage.GetExtensionBCL(fileName)))
            {
                item.Filename = fileName;
                return fileName;
            }

            string extension = ExternalContentManager.GetEntryTypeExtensions(type).FirstOrDefault() ?? string.Empty;
            item.Filename = fileName + extension;
            return item.Filename;
        }

        public static void ApplyItemFileInfo(ModpackItem item, PostFileItem communityFile)
        {
            if (item == null || communityFile == null)
            {
                return;
            }

            string apiFileName = CommunityFileRules.GetDownloadFileName(communityFile);
            if (!item.HasExplicitFileName && !string.IsNullOrWhiteSpace(apiFileName))
            {
                item.Filename = Storage.GetFileName(apiFileName);
            }
            if (string.IsNullOrWhiteSpace(item.PackageName) && !string.IsNullOrWhiteSpace(communityFile.PackageName))
            {
                item.PackageName = communityFile.PackageName.Trim();
            }
            if (string.IsNullOrWhiteSpace(item.Version) && !string.IsNullOrWhiteSpace(communityFile.PackageVersion))
            {
                item.Version = communityFile.PackageVersion.Trim();
            }
        }

        private static string NormalizeArchiveEntryPath(string path)
        {
            string normalized = (path ?? string.Empty).Trim().Replace('\\', '/');
            while (normalized.StartsWith("/", StringComparison.Ordinal))
            {
                normalized = normalized.Substring(1);
            }

            List<string> segments = [];
            foreach (string rawSegment in normalized.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                string segment = rawSegment.Trim();
                if (segment == "..")
                {
                    return null;
                }
                if (!string.IsNullOrWhiteSpace(segment) && segment != ".")
                {
                    segments.Add(segment);
                }
            }
            return string.Join("/", segments);
        }

        private static string GetDisplayName(ModpackItem item)
        {
            return item == null
                ? LanguageControl.Get("ScbbsPlusModpackRules", "PackItemDefaultName")
                : item.Filename ?? item.FileId ?? item.Url ?? item.Category ?? LanguageControl.Get("ScbbsPlusModpackRules", "PackItemDefaultName");
        }

        private static ExternalContentType ResolveCategory(string category)
        {
            return (category ?? string.Empty).ToLowerInvariant() switch
            {
                "world" or "scworld" => ExternalContentType.World,
                "furniture" or "scfpack" => ExternalContentType.FurniturePack,
                "texture" or "scbtex" or "png" => ExternalContentType.BlocksTexture,
                "skin" or "scskin" => ExternalContentType.CharacterSkin,
                "mod" or "scmod" => ExternalContentType.Mod,
                _ => ExternalContentType.Unknown
            };
        }
    }
}
