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
                // 服务端 filename 可能由上传 saveKey（转义路径）反推，含 %5B %20 等转义：
                // 落盘前先解码再净化；非法转义序列（如字面 "%"）按原值保留（与匹配侧归一同策略）。
                return CommunityUploadFileNaming.SanitizeFileName(UnescapeFileName(file.Filename));
            }
            try
            {
                if (Uri.TryCreate(file.Url, UriKind.Absolute, out Uri uri))
                {
                    return CommunityUploadFileNaming.SanitizeFileName(
                        Uri.UnescapeDataString(Storage.GetFileName(uri.LocalPath)));
                }
            }
            catch
            {
                // URL 尾段含非法转义等：回退 id 命名。
            }
            return $"download_{file.Id}";
        }

        private static string UnescapeFileName(string value)
        {
            try
            {
                return Uri.UnescapeDataString(value);
            }
            catch (UriFormatException)
            {
                return value;
            }
        }
    }
}
