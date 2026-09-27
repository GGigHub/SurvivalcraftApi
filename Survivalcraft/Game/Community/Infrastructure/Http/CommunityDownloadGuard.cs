using System;
using System.Globalization;

namespace Game
{
    /// <summary>
    /// 下载防护：响应体大小上限。用于第三方来源 URL（整合包清单）与统一下载器，防内存耗尽。
    /// </summary>
    internal static class CommunityDownloadGuard
    {
        // 整合包子项/附件均为几十 MB 量级；256MB 覆盖全部合法场景并防住异常超大响应。
        public const long MaxPayloadBytes = 256L * 1024 * 1024;

        public static void EnsurePayloadWithinLimit(long? contentLength)
        {
            if (contentLength.HasValue && contentLength.Value > MaxPayloadBytes)
            {
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusCommunityDownloadGuard", "DownloadExceedsSizeLimit"), MaxPayloadBytes));
            }
        }
    }
}
