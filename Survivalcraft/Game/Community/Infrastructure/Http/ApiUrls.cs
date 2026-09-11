using System;

namespace Game
{
    /// <summary>
    /// 社区 API 基址的唯一定义与归一化：设置存储与各 HTTP 客户端共用。
    /// 归一化保证结果要么是原绝对 URL，要么回退默认服务器——绝不产出相对 URL
    /// （HttpClient 收到相对 URL 会直接抛 InvalidRequestUri）。
    /// </summary>
    internal static class ApiUrls
    {
        internal const string DefaultBaseUrl = "https://m.suancaixianyu.cn/api";

        internal static string NormalizeBaseUrl(string value)
        {
            string normalized = (value ?? string.Empty).Trim();
            while (normalized.EndsWith("/", StringComparison.Ordinal))
            {
                normalized = normalized[..^1];
            }

            return normalized.Length == 0 ? DefaultBaseUrl : normalized;
        }
    }
}
