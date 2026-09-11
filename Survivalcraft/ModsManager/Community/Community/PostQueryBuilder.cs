using System;
using System.Collections.Generic;
using System.Globalization;

namespace Game
{
    internal static class PostQueryBuilder
    {
        public static List<string> BuildCommunityQueryParts(
            int page,
            ScbbsPlusCommunityFileType fileType,
            CommunityOrderType orderType,
            string searchTitle,
            bool ignoreOrder,
            bool ignoreFileType)
        {
            bool useSearchIgnore = !string.IsNullOrEmpty(searchTitle);
            List<string> queryParts = ["type=2", $"page={Math.Max(page, 1)}", "limit=10"];
            if (!useSearchIgnore || !ignoreOrder)
            {
                queryParts.Add($"orderType={Uri.EscapeDataString(((int)orderType).ToString(CultureInfo.InvariantCulture))}");
            }
            if (!useSearchIgnore || !ignoreFileType)
            {
                queryParts.Add($"fileTypes={(int)fileType}");
            }
            if (!string.IsNullOrEmpty(searchTitle))
            {
                queryParts.Add($"title={Uri.EscapeDataString(searchTitle.Trim())}");
            }
            return queryParts;
        }
    }
}
