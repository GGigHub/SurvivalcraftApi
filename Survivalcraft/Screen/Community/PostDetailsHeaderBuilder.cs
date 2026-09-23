using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using static Game.PostDetailsContentBuilder;

namespace Game
{
    /// <summary>
    /// 详情弹窗 header 区构建器：本地结构化数据直接产 widget（不经 HTML 拼接再解析），
    /// 不进入自动翻译载荷。content 区（服务端 HTML / 纯文本）由对话框负责。
    /// </summary>
    internal static class PostDetailsHeaderBuilder
    {
        // 更新弹窗通用元信息：可选标题行 + 当前版本 / 最新版本 / 下载文件名。
        public static void AppendUpdateInfo(StackPanelWidget panel, string title, string currentVersion, string latestVersion, string downloadFileName)
        {
            AppendHeading(panel, title);
            AppendParagraph(panel, string.Format(
                CultureInfo.InvariantCulture,
                "{0}：{1}",
                LanguageControl.Get("ScbbsPlusPostDetailsHeaderBuilder", "Text", "CurrentVersion"),
                string.IsNullOrWhiteSpace(currentVersion) ? "-" : currentVersion));
            AppendParagraph(panel, string.Format(
                CultureInfo.InvariantCulture,
                "{0}：{1}",
                LanguageControl.Get("ScbbsPlusPostDetailsHeaderBuilder", "Text", "LatestVersion"),
                string.IsNullOrWhiteSpace(latestVersion) ? "-" : latestVersion));
            AppendParagraph(panel, string.Format(
                CultureInfo.InvariantCulture,
                "{0}：{1}",
                LanguageControl.Get("ScbbsPlusPostDetailsHeaderBuilder", "Text", "DownloadFile"),
                string.IsNullOrWhiteSpace(downloadFileName) ? "-" : downloadFileName));
        }

        // 「我发布的」帖子详情：下载统计摘要 + 最近下载趋势 + 版本下载量。
        public static void AppendPublishStats(StackPanelWidget panel, PostDownloadTrendResponse stats, string statsError)
        {
            AppendHeading(panel, LanguageControl.Get("ScbbsPlusCommunityScreen", "Community", "Posts", "PublishStats"));
            if (stats == null)
            {
                AppendParagraph(panel, string.Format(
                    CultureInfo.InvariantCulture,
                    LanguageControl.Get("ScbbsPlusCommunityScreen", "Community", "Posts", "FailedToLoadStats"),
                    string.IsNullOrWhiteSpace(statsError) ? LanguageControl.Get("ScbbsPlusPostDetailsHeaderBuilder", "Text", "UnknownError") : statsError));
                return;
            }

            AppendParagraph(panel, string.Format(
                CultureInfo.InvariantCulture,
                LanguageControl.Get("ScbbsPlusCommunityScreen", "Community", "Posts", "Last30DaysTotalVersionsFiles"),
                stats.TotalDownloadCount,
                stats.Post?.TotalDownloadCount ?? 0,
                stats.Post?.VersionCount ?? stats.Versions?.Length ?? 0,
                stats.Post?.FileCount ?? 0));
            AppendRecentDownloadSeries(panel, stats.Series);
            AppendVersionDownloadStats(panel, stats.Versions);
        }

        private static void AppendRecentDownloadSeries(StackPanelWidget panel, PostDownloadTrendSeriesItem[] series)
        {
            IReadOnlyList<PostDownloadTrendSeriesItem> visibleSeries = series?
                .Where(item => item != null && item.DownloadCount > 0)
                .TakeLast(7)
                .ToArray();
            if (visibleSeries == null || visibleSeries.Count == 0)
            {
                AppendParagraph(panel, LanguageControl.Get("ScbbsPlusCommunityScreen", "Community", "Posts", "NoDownloadsInTheLast30Days"));
                return;
            }

            AppendHeading(panel, LanguageControl.Get("ScbbsPlusCommunityScreen", "Community", "Posts", "RecentDownloads"));
            foreach (PostDownloadTrendSeriesItem item in visibleSeries)
            {
                AppendListItem(panel, string.Format(
                    CultureInfo.InvariantCulture,
                    LanguageControl.Get("ScbbsPlusCommunityScreen", "Community", "Posts", "DailyDownloadsFormat"),
                    CommunityTimeFormatter.FormatDate(item.BucketStart),
                    item.DownloadCount));
            }
        }

        private static void AppendVersionDownloadStats(StackPanelWidget panel, PostDownloadTrendVersion[] versions)
        {
            if (versions == null || versions.Length == 0)
            {
                return;
            }

            AppendHeading(panel, LanguageControl.Get("ScbbsPlusCommunityScreen", "Community", "Posts", "VersionDownloads"));
            foreach (PostDownloadTrendVersion version in versions.Where(version => version != null).OrderByDescending(version => version.RangeDownloadCount).ThenByDescending(version => version.TotalDownloadCount).Take(6))
            {
                string name = !string.IsNullOrWhiteSpace(version.Version)
                    ? version.Version.Trim()
                    : (!string.IsNullOrWhiteSpace(version.Title) ? version.Title.Trim() : LanguageControl.Get("ScbbsPlusPostDetailsHeaderBuilder", "Text", "UnnamedVersion"));
                AppendListItem(panel, string.Format(
                    CultureInfo.InvariantCulture,
                    LanguageControl.Get("ScbbsPlusCommunityScreen", "Community", "Posts", "VersionLast30DaysTotalFiles"),
                    name,
                    version.RangeDownloadCount,
                    version.TotalDownloadCount,
                    version.FileCount));
            }
        }
    }
}
