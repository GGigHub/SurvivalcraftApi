using System.Collections.Generic;

namespace Game
{
    public class PostsLoadResult
    {
        public int RequestId { get; set; }
        public ScbbsPlusCommunityFileType FileType { get; set; }
        public ResourceSource Source { get; set; }
        // 发起加载的服务器地址：目录缓存据此绑定来源。
        public string ApiBaseUrl { get; set; }
        public PostsResponse Response { get; set; }
        public VersionListItem[] VersionCatalog { get; set; }
        public Dictionary<int, string> GameVersionsById { get; set; }
        public string Error { get; set; }
    }

    public class PostDetailLoadResult
    {
        public int RequestId { get; set; }
        public PostDetailResponse Response { get; set; }
        public string Error { get; set; }
    }

    public class PostDetailsDialogLoadResult
    {
        public int RequestId { get; set; }
        public PostItem SourcePost { get; set; }
        public PostVersionItem SourceVersion { get; set; }
        public PostVersionItem ResolvedVersion { get; set; }
        public bool ShowVersionDetails { get; set; }
        public PostDetailResponse Response { get; set; }
        public PostDownloadTrendResponse Stats { get; set; }
        public bool IncludeStats { get; set; }
        public string StatsError { get; set; }
        public string Error { get; set; }
    }

    public class FeaturedLoadResult
    {
        public int RequestId { get; set; }
        public IReadOnlyList<PostItem> RecentUpdates { get; set; }
        public IReadOnlyList<PostItem> TopResources { get; set; }
        public string Error { get; set; }
    }
}
