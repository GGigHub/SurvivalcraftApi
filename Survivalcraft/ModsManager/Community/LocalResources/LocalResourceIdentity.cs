using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Game
{
    internal static class LocalResourceIdentity
    {
        public static string GetModStoragePath(LocalResourceItem resourceItem)
        {
            return resourceItem?.LocalFilePath
                ?? resourceItem?.ModEntity?.ModFilePath;
        }

        public static string MakeModReviewKey(LocalResourceItem resourceItem)
        {
            if (!string.IsNullOrWhiteSpace(resourceItem?.ModEntity?.modInfo?.PackageName))
            {
                return resourceItem.ModEntity.modInfo.PackageName;
            }
            if (!string.IsNullOrWhiteSpace(resourceItem?.ModEntity?.ModFilePath))
            {
                return resourceItem.ModEntity.ModFilePath;
            }
            if (resourceItem?.Type != ExternalContentType.Mod)
            {
                string resourceName = !string.IsNullOrWhiteSpace(resourceItem?.Name)
                    ? resourceItem.Name
                    : resourceItem?.DisplayName;
                return string.IsNullOrWhiteSpace(resourceName)
                    ? string.Empty
                    : $"{resourceItem.Type}:{resourceName}";
            }
            if (!string.IsNullOrWhiteSpace(resourceItem?.Name))
            {
                return resourceItem.Name;
            }
            return resourceItem?.DisplayName ?? string.Empty;
        }

        public static string MakeWorldReviewKey(WorldInfo worldInfo)
        {
            if (!string.IsNullOrWhiteSpace(worldInfo?.DirectoryName))
            {
                return $"world:{worldInfo.DirectoryName}";
            }
            return string.IsNullOrWhiteSpace(worldInfo?.WorldSettings?.Name)
                ? string.Empty
                : $"world:{worldInfo.WorldSettings.Name}";
        }

        public static string BuildModAutoUpdateScanSignature(IEnumerable<LocalResourceItem> resources)
        {
            StringBuilder builder = new();
            foreach (LocalResourceItem resource in (resources ?? [])
                         .Where(item => item?.Type == ExternalContentType.Mod)
                         .OrderBy(MakeModSignatureKey, StringComparer.OrdinalIgnoreCase))
            {
                builder.Append(MakeModSignatureKey(resource));
                builder.Append('|');
                builder.Append(resource?.ModEntity?.modInfo?.Version ?? resource?.Version ?? string.Empty);
                builder.Append(';');
            }
            return builder.ToString();
        }

        private static string MakeModSignatureKey(LocalResourceItem resourceItem)
        {
            return resourceItem?.PackageName
                   ?? resourceItem?.ModEntity?.modInfo?.PackageName
                   ?? resourceItem?.LocalFilePath
                   ?? resourceItem?.Name
                   ?? string.Empty;
        }
    }
}
