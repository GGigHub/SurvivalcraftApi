using System;

namespace Game
{
    /// <summary>
    /// 社区 API 基址的归一化与历史旧值升级：设置存储与各 HTTP 客户端共用。
    /// 归一化保证结果要么是原绝对 URL，要么回退默认服务器——绝不产出相对 URL
    /// （HttpClient 收到相对 URL 会直接抛 InvalidRequestUri）。
    /// 默认服务器唯一来源于 CommunityServerManager.DefaultChineseInfo（本体迁服只改它）；
    /// 历史上作为默认值出现过的地址（别名表）在读取时自动升级为当前默认。
    /// </summary>
    internal static class ApiUrls
    {
        // 历史默认值（归一化形式）：m.schub.top 为最老默认，m.suancaixianyu.cn 根域为过渡默认。
        // 只收历史默认值；模组时代的无斜杠 /api 常量归一化后即当前默认，无须入表。
        private static readonly string[] s_legacyDefaults =
        {
            "https://m.schub.top",
            "https://m.suancaixianyu.cn"
        };

        internal static string DefaultBaseUrl => NormalizeBaseUrl(CommunityServerManager.DefaultChineseInfo.ApiUrl);

        internal static string NormalizeBaseUrl(string value)
        {
            string normalized = (value ?? string.Empty).Trim();
            while (normalized.EndsWith("/", StringComparison.Ordinal))
            {
                normalized = normalized[..^1];
            }

            if (normalized.Length == 0)
            {
                return DefaultBaseUrl;
            }

            foreach (string legacyDefault in s_legacyDefaults)
            {
                if (string.Equals(normalized, legacyDefault, StringComparison.OrdinalIgnoreCase))
                {
                    return DefaultBaseUrl;
                }
            }

            return normalized;
        }
    }
}
