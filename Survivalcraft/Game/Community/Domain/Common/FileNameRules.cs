using System;
using Engine;

namespace Game
{
    internal static class FileNameRules
    {
        public static string GetExtension(string path)
        {
            string fileName = Storage.GetFileName(path ?? string.Empty);
            string extension = Storage.GetExtension(fileName);
            return string.Equals(fileName, extension, StringComparison.Ordinal)
                ? string.Empty
                : extension;
        }

        public static string GetFileNameWithoutExtension(string path)
        {
            string fileName = Storage.GetFileName(path ?? string.Empty);
            string extension = GetExtension(fileName);
            return string.IsNullOrEmpty(extension)
                ? fileName
                : fileName[..^extension.Length];
        }

        // 社区可控文件名（Url/Filename）匹配前的归一化：去 URL 转义、统一分隔符、取末段。
        public static string NormalizeRemoteFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            string text = value.Trim();
            try
            {
                text = Uri.UnescapeDataString(text);
            }
            catch (UriFormatException)
            {
                // 非法转义序列（如单独 "%"），回退原始裁剪值继续比较。
            }
            return Storage.GetFileName(text);
        }
    }
}
