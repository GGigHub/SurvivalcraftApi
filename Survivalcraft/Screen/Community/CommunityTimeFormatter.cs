using System;
using System.Globalization;

namespace Game
{
    /// <summary>
    /// API 时间戳 → 用户可见文本的唯一入口（仅限 UI 展示）：
    /// 解析固定 InvariantCulture + RoundtripKind，防设备非公历区域（fa-IR 波斯历等）
    /// 劫持年份解读；显示按 CurrentCulture 当地习惯（"g" 日期+时刻 / "d" 仅日期），
    /// 不再硬编码 "yyyy-MM-dd" 模板。发服务器的字段（如 PlayedAt）自行用 "O" 序列化，
    /// 不得经此类。
    /// </summary>
    internal static class CommunityTimeFormatter
    {
        internal static string FormatTime(string value)
        {
            return TryParse(value, out DateTime dateTime)
                ? dateTime.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
                : "-";
        }

        internal static string FormatDate(string value)
        {
            return TryParse(value, out DateTime dateTime)
                ? dateTime.ToLocalTime().ToString("d", CultureInfo.CurrentCulture)
                : value ?? string.Empty;
        }

        private static bool TryParse(string value, out DateTime dateTime)
        {
            return DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out dateTime);
        }
    }
}
