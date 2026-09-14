using System;
using System.Globalization;
using System.Net;
using System.Text.Json;

namespace Game
{
    /// <summary>
    /// 帖子版本详情内容提取与版本文本格式化：Content/ExtensionData 里的服务端内容
    /// 原样透传给详情弹窗 content 区（弹窗与列表渲染复用其 helper）。
    /// </summary>
    internal static class CommunityVersionDetailFormatter
    {
        public static string GetVersionDetailHtml(PostVersionItem version)
        {
            if (!string.IsNullOrWhiteSpace(version?.Content))
            {
                return version.Content;
            }

            if (version?.ExtensionData != null)
            {
                string[] keys = ["content", "detail", "description", "remark", "body"];
                foreach (string key in keys)
                {
                    if (version.ExtensionData.TryGetValue(key, out JsonElement element) && element.ValueKind == JsonValueKind.String)
                    {
                        string value = element.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            return value;
                        }
                    }
                }
            }

            string versionText = FormatEmpty(version?.Version);
            string versionLine = versionText != "-" && !IsIntegerText(versionText)
                ? string.Format(CultureInfo.CurrentCulture, "<p>{0}：{1}</p>", LanguageControl.Get("ScbbsPlusModVersionsDialog", "Version"), WebUtility.HtmlEncode(versionText))
                : string.Empty;
            return string.Format(
                CultureInfo.CurrentCulture,
                "<p>{0}</p>{1}<p>{2}：{3}</p>",
                WebUtility.HtmlEncode(FormatVersionTitle(version)),
                versionLine,
                LanguageControl.Get("ScbbsPlusModVersionsDialog", "Published"),
                WebUtility.HtmlEncode(CommunityTimeFormatter.FormatTime(version?.CreatedAt)));
        }

        internal static string FormatVersionTitle(PostVersionItem version)
        {
            string title = FormatEmpty(version?.Title);
            string versionText = FormatEmpty(version?.Version);
            if (title != "-")
            {
                return title;
            }
            return versionText != "-" && !IsIntegerText(versionText) ? versionText : LanguageControl.Get("ScbbsPlusVersionDetailFormatter", "Text", "UnnamedVersion");
        }

        internal static bool IsIntegerText(string value)
        {
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
        }

        internal static string FormatEmpty(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
        }
    }
}
