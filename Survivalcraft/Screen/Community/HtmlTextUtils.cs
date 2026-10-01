using System.Text.RegularExpressions;

namespace Game {
    // 帖子详情 HTML 走文本化渲染时的纯文本规整与列表标记计算。
    // 与 widget 构造解耦，便于单测。
    static class HtmlTextUtils {
        // 纯文本（Gitee release notes 等）逐行解析："- "/"* " 开头识别为列表项，空行丢弃。
        // 渲染（widget 产出）由 ContentBuilder.AppendPlainText 按此结果执行。
        public static List<(bool IsListItem, string Text)> SplitPlainTextLines(string text) {
            List<(bool, string)> lines = [];
            if (string.IsNullOrWhiteSpace(text)) {
                return lines;
            }
            foreach (string rawLine in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')) {
                string line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line)) {
                    continue;
                }
                if (line.StartsWith("- ", StringComparison.Ordinal)
                    || line.StartsWith("* ", StringComparison.Ordinal)) {
                    lines.Add((true, line[2..].Trim()));
                }
                else {
                    lines.Add((false, line));
                }
            }
            return lines;
        }

        // 行内文本：连续空白（含 nbsp）折叠为单空格。
        public static string NormalizeInline(string text) => Regex.Replace(text ?? string.Empty, "[\\s\\u00a0]+", " ");

        // 块级收尾：折叠空格、去行首尾空白、3+ 连续换行压成 2。
        public static string NormalizeBlock(string text) {
            text = Regex.Replace(text ?? string.Empty, "[ \\t\\u00a0]+", " ");
            text = Regex.Replace(text, "\\n[ \\t]+", "\n");
            text = Regex.Replace(text, "\\n{3,}", "\n\n");
            return text;
        }

        // 无序列表标记按层级：• / ◦ / 第三级及更深 ▪；有序列表 "1." "2." …（number 由调用方按列表维护计数）。
        public static string GetListMarker(bool ordered, int number, int depth) {
            if (ordered) {
                return $"{number}.";
            }
            return depth switch {
                <= 1 => "•",
                2 => "◦",
                _ => "▪"
            };
        }
    }
}