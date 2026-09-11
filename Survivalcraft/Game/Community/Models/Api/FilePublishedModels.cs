using System.Text.Json.Serialization;

namespace Game
{
    public class FilePublishedResponse : ApiEnvelope<FilePublishedData>
    {
    }

    public class FilePublishedData
    {
        [JsonPropertyName("filename")] public string Filename { get; set; }
        [JsonPropertyName("published")] public bool Published { get; set; }
        [JsonPropertyName("total")] public int Total { get; set; }
        [JsonPropertyName("posts")] public FilePublishedPost[] Posts { get; set; }
    }

    public class FilePublishedPost
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("title")] public string Title { get; set; }
        [JsonPropertyName("fileType")]
        [JsonConverter(typeof(CommunityFileTypeJsonConverter))]
        public CommunityFileType FileType { get; set; }
        // version/file 复用 PostModels 的完整模型：服务端这里是子集，缺省字段反序列化为 null。
        [JsonPropertyName("version")] public PostVersionItem Version { get; set; }
        [JsonPropertyName("file")] public PostFileItem File { get; set; }
    }
}
