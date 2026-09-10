using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace Game
{
    /// <summary>
    /// 下载防护：响应体大小上限 + 出站主机策略。用于第三方来源 URL（整合包清单）
    /// 与统一下载器，防内存耗尽与 SSRF 内网直连。
    /// </summary>
    internal static class DownloadGuard
    {
        // 整合包子项/附件均为几十 MB 量级；256MB 覆盖全部合法场景并防住异常超大响应。
        public const long MaxPayloadBytes = 256L * 1024 * 1024;

        public static void EnsurePayloadWithinLimit(long? contentLength)
        {
            if (contentLength.HasValue && contentLength.Value > MaxPayloadBytes)
            {
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusCommunityScreen", "Community", "Posts", "DownloadExceedsSizeLimit"), MaxPayloadBytes));
            }
        }

        /// <summary>
        /// 拒绝解析到环回/私网/链路本地的出站地址（云元数据、内网管理接口等 SSRF 目标）。
        /// IP 字面量直接判定；域名做 DNS 解析后判定（尽力而为，解析失败放行——请求
        /// 本身会再解析一次并失败，不构成额外暴露面）。
        /// </summary>
        public static void EnsureAllowedHost(string url)
        {
            if (string.IsNullOrWhiteSpace(url)
                || !Uri.TryCreate(url, UriKind.Absolute, out Uri uri)
                || !uri.IsAbsoluteUri)
            {
                return;
            }

            string host = uri.Host;
            if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusCommunityScreen", "Community", "Posts", "LocalHostUrlForbidden"), url));
            }
            if (IPAddress.TryParse(host, out IPAddress literal))
            {
                if (IsPrivateOrLinkLocal(literal))
                {
                    throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusCommunityScreen", "Community", "Posts", "InternalNetworkUrlForbidden"), url));
                }
                return;
            }

            try
            {
                IPAddress[] addresses = Dns.GetHostAddresses(host);
                if (addresses.Any(IsPrivateOrLinkLocal))
                {
                    throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusCommunityScreen", "Community", "Posts", "InternalNetworkUrlForbidden"), url));
                }
            }
            catch (SocketException)
            {
                // 域名解析失败：请求本身也会失败，按尽力而为处理放行。
            }
        }

        private static bool IsPrivateOrLinkLocal(IPAddress address)
        {
            // IPv4-mapped IPv6（::ffff:/96 前缀，AddressFamily 为 InterNetworkV6）先归一化为
            // IPv4 再判定，且必须先于 IsLoopback：::ffff:127.x.y.z（除 .NET 特判的
            // ::ffff:127.0.0.1 外）不命中 IsLoopback，归一化后才能进 127/8 拒绝。
            // 仅对真映射地址调用 MapToIPv4：非映射地址该属性为假，避免取尾 4 字节误判。
            if (address.IsIPv4MappedToIPv6)
            {
                address = address.MapToIPv4();
            }
            if (IPAddress.IsLoopback(address))
            {
                return true;
            }
            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                byte[] bytes = address.GetAddressBytes();
                return bytes[0] == 0
                    || bytes[0] == 10
                    || (bytes[0] == 100 && (bytes[1] & 0xC0) == 0x40)
                    || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                    || (bytes[0] == 192 && bytes[1] == 168)
                    || (bytes[0] == 169 && bytes[1] == 254);
            }
            byte[] ipv6Bytes = address.GetAddressBytes();
            return address.IsIPv6LinkLocal
                || (ipv6Bytes[0] >= 0xFC && ipv6Bytes[0] <= 0xFD);
        }
    }
}
