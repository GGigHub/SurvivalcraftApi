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

        public static ExternalContentType MapFileTypeToExternalContentType(ScbbsPlusCommunityFileType fileType)
        {
            return fileType switch
            {
                ScbbsPlusCommunityFileType.World => ExternalContentType.World,
                ScbbsPlusCommunityFileType.Furniture => ExternalContentType.FurniturePack,
                ScbbsPlusCommunityFileType.Texture => ExternalContentType.BlocksTexture,
                ScbbsPlusCommunityFileType.Skin => ExternalContentType.CharacterSkin,
                ScbbsPlusCommunityFileType.Mod or ScbbsPlusCommunityFileType.NetMod => ExternalContentType.Mod,
                _ => ExternalContentType.Unknown
            };
        }

        public static ScbbsPlusCommunityFileType MapExternalContentTypeToFileType(ExternalContentType type)
        {
            return type switch
            {
                ExternalContentType.World => ScbbsPlusCommunityFileType.World,
                ExternalContentType.FurniturePack => ScbbsPlusCommunityFileType.Furniture,
                ExternalContentType.BlocksTexture => ScbbsPlusCommunityFileType.Texture,
                ExternalContentType.CharacterSkin => ScbbsPlusCommunityFileType.Skin,
                ExternalContentType.Mod => ScbbsPlusCommunityFileType.Mod,
                _ => ScbbsPlusCommunityFileType.Unknown
            };
        }
    }
}
