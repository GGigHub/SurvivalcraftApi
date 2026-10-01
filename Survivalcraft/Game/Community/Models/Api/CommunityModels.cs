using System;
using System.Text.Json.Serialization;
using NuGet.Versioning;

namespace Game
{
    public class BasicApiResponse : ApiEnvelope<object>
    {
    }

    public class PostsResponse : ApiEnvelope<PostsData>
    {
    }

    public class PostsData
    {
        [JsonPropertyName("list")] public PostItem[] List { get; set; }
        [JsonPropertyName("total")] public int Total { get; set; }
        [JsonPropertyName("page")] public int Page { get; set; }
        [JsonPropertyName("limit")] public int Limit { get; set; }
        [JsonPropertyName("count")] public int Count { get; set; }
    }

    public class VersionListResponse : ApiEnvelope<VersionListItem[]>
    {
    }

    public class VersionListItem
    {
        private string m_version;

        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; }
        [JsonPropertyName("type")] public string Type { get; set; }
        [JsonPropertyName("version")]
        public string Version
        {
            get => m_version;
            set
            {
                m_version = value;
                NuGetVersion = ExternalVersionParser.ParseOrNull(value);
                IsVersionLine = IsTwoComponentRelease(NuGetVersion);
            }
        }
        [JsonIgnore] public NuGetVersion NuGetVersion { get; private set; }
        [JsonIgnore] public bool IsVersionLine { get; private set; }
        [JsonPropertyName("status")] public int Status { get; set; }

        private static bool IsTwoComponentRelease(NuGetVersion version)
        {
            return version != null
                && !version.IsPrerelease
                && string.IsNullOrEmpty(version.Metadata)
                && System.Version.TryParse(version.OriginalVersion, out Version originalVersion)
                && originalVersion.Build < 0;
        }
    }

    public class PostDetailResponse : ApiEnvelope<PostItem>
    {
    }

    public class PostDownloadTrendApiResponse : ApiEnvelope<PostDownloadTrendResponse>
    {
    }

    public class PostDownloadTrendResponse
    {
        [JsonPropertyName("range")] public PostDownloadTrendRange Range { get; set; }
        [JsonPropertyName("granularity")] public string Granularity { get; set; }
        [JsonPropertyName("post")] public PostDownloadTrendPost Post { get; set; }
        [JsonPropertyName("totalDownloadCount")] public int TotalDownloadCount { get; set; }
        [JsonPropertyName("series")] public PostDownloadTrendSeriesItem[] Series { get; set; }
        [JsonPropertyName("versions")] public PostDownloadTrendVersion[] Versions { get; set; }
    }

    public class PostDownloadTrendRange
    {
        [JsonPropertyName("startTime")] public string StartTime { get; set; }
        [JsonPropertyName("endTime")] public string EndTime { get; set; }
    }

    public class PostDownloadTrendPost
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("title")] public string Title { get; set; }
        [JsonPropertyName("totalDownloadCount")] public int TotalDownloadCount { get; set; }
        [JsonPropertyName("versionCount")] public int VersionCount { get; set; }
        [JsonPropertyName("fileCount")] public int FileCount { get; set; }
    }

    public class PostDownloadTrendSeriesItem
    {
        [JsonPropertyName("bucketStart")] public string BucketStart { get; set; }
        [JsonPropertyName("downloadCount")] public int DownloadCount { get; set; }
    }

    public class PostDownloadTrendVersion
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("postId")] public int PostId { get; set; }
        [JsonPropertyName("title")] public string Title { get; set; }
        [JsonPropertyName("version")] public string Version { get; set; }
        [JsonPropertyName("fileCount")] public int FileCount { get; set; }
        [JsonPropertyName("rangeDownloadCount")] public int RangeDownloadCount { get; set; }
        [JsonPropertyName("totalDownloadCount")] public int TotalDownloadCount { get; set; }
        [JsonPropertyName("files")] public PostDownloadTrendFile[] Files { get; set; }
    }

    public class PostDownloadTrendFile
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("filename")] public string Filename { get; set; }
        [JsonPropertyName("totalDownloadCount")] public int TotalDownloadCount { get; set; }
        [JsonPropertyName("rangeDownloadCount")] public int RangeDownloadCount { get; set; }
    }

    public class FileInfoResponse : ApiEnvelope<PostFileItem>
    {
    }
}
