using System;
using Engine;

namespace Game
{
    internal static class CommunityFileRules
    {
        public static string GetDownloadFileName(PostFileItem file)
        {
            if (file == null)
            {
                return null;
            }
            if (!string.IsNullOrEmpty(file.Filename))
            {
                // 社区可控文件名出口统一净化（保留名/非法字符/尾点/超长），防止污染本地落盘路径。
                return UploadFileNaming.SanitizeFileName(file.Filename);
            }
            try
            {
                if (Uri.TryCreate(file.Url, UriKind.Absolute, out Uri uri))
                {
                    return UploadFileNaming.SanitizeFileName(
                        Uri.UnescapeDataString(Storage.GetFileName(uri.LocalPath)));
                }
            }
            catch
            {
                // URL 尾段含非法转义等：回退 id 命名。
            }
            return $"download_{file.Id}";
        }

        public static ExternalContentType MapFileTypeToExternalContentType(CommunityFileType fileType)
        {
            return fileType switch
            {
                CommunityFileType.World => ExternalContentType.World,
                CommunityFileType.Furniture => ExternalContentType.FurniturePack,
                CommunityFileType.Texture => ExternalContentType.BlocksTexture,
                CommunityFileType.Skin => ExternalContentType.CharacterSkin,
                CommunityFileType.Mod or CommunityFileType.NetMod => ExternalContentType.Mod,
                _ => ExternalContentType.Unknown
            };
        }

        public static CommunityFileType MapExternalContentTypeToFileType(ExternalContentType type)
        {
            return type switch
            {
                ExternalContentType.World => CommunityFileType.World,
                ExternalContentType.FurniturePack => CommunityFileType.Furniture,
                ExternalContentType.BlocksTexture => CommunityFileType.Texture,
                ExternalContentType.CharacterSkin => CommunityFileType.Skin,
                ExternalContentType.Mod => CommunityFileType.Mod,
                _ => CommunityFileType.Unknown
            };
        }
    }
}
