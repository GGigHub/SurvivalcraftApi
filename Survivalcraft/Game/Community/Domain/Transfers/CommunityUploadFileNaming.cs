using System.Globalization;
using Engine;

namespace Game {
    static class CommunityUploadFileNaming {
        public static string MakeWorldFileName(string worldName) => MakeSafeFileName(worldName, ".scworld");

        public static string MakeSafeFileName(string displayName, string extension, string fallback = "resource") {
            string safeName = Storage.SanitizeFileNameSegment(displayName, fallback);
            if (!extension.StartsWith(".", StringComparison.Ordinal)) {
                extension = "." + extension;
            }
            return safeName.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? safeName : $"{safeName}{extension}";
        }

        public static string SanitizeFileName(string fileName) {
            string safeName = Storage.SanitizeFileNameSegment(fileName);
            // 空输入时的历史默认值带扩展名，保持兼容。
            return safeName == "resource" ? "resource.dat" : safeName;
        }

        public static string MakeSaveKey(string fileName, DateTime utcNow) {
            string safeFileName = Uri.EscapeDataString(SanitizeFileName(fileName));
            string datePath = utcNow.ToUniversalTime().ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
            return $"/{datePath}/{safeFileName}";
        }
    }
}