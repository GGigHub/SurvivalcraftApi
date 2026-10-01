using NuGet.Versioning;

namespace Game {
    /// <summary>
    /// 将外部版本文本规范化并解析为 NuGetVersion。
    /// </summary>
    static class ExternalVersionParser {
        public static string Normalize(string version) {
            version = version?.Trim();
            if (string.IsNullOrEmpty(version)) {
                return version;
            }
            for (int i = 0; i < version.Length; i++) {
                if (char.IsDigit(version[i])) {
                    return version.Substring(i);
                }
            }
            return version;
        }

        public static bool TryParse(string version, out NuGetVersion parsed) {
            parsed = null;
            string text = Normalize(version);
            return !string.IsNullOrEmpty(text) && NuGetVersion.TryParse(text, out parsed);
        }

        // 供模型 setter 一行完成"赋值即解析"：解析失败存 null。
        public static NuGetVersion ParseOrNull(string version) => TryParse(version, out NuGetVersion parsed) ? parsed : null;
    }
}