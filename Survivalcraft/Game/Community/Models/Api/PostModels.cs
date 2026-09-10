using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using NuGet.Versioning;

namespace Game
{
    public class PostItem
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("title")] public string Title { get; set; }
        [JsonPropertyName("content")] public string Content { get; set; }
        [JsonPropertyName("views")] public int Views { get; set; }
        [JsonPropertyName("createdAt")] public string CreatedAt { get; set; }
        [JsonPropertyName("plateId")] public int PlateId { get; set; }
        [JsonPropertyName("creatorId")] public int CreatorId { get; set; }
        [JsonPropertyName("fileType")]
        [JsonConverter(typeof(CommunityFileTypeJsonConverter))]
        public ScbbsPlusCommunityFileType FileType { get; set; }
        [JsonPropertyName("downloadCount")] public int DownloadCount { get; set; }
        [JsonPropertyName("type")] public int Type { get; set; }
        [JsonPropertyName("cover")] public string Cover { get; set; }
        [JsonPropertyName("icon")] public string Icon { get; set; }
        [JsonPropertyName("visible")] public int Visible { get; set; }
        [JsonPropertyName("status")] public int Status { get; set; }
        // 计数字段与 Size 一致：服务端类型不稳定，数字值经 FlexibleStringJsonConverter 归一化为字符串。
        [JsonPropertyName("likeCount")] [JsonConverter(typeof(FlexibleStringJsonConverter))] public string LikeCount { get; set; }
        [JsonPropertyName("badCount")] [JsonConverter(typeof(FlexibleStringJsonConverter))] public string BadCount { get; set; }
        [JsonPropertyName("favCount")] [JsonConverter(typeof(FlexibleStringJsonConverter))] public string FavCount { get; set; }
        [JsonPropertyName("postVersionCount")] [JsonConverter(typeof(FlexibleStringJsonConverter))] public string PostVersionCount { get; set; }
        [JsonPropertyName("commentCount")] [JsonConverter(typeof(FlexibleStringJsonConverter))] public string CommentCount { get; set; }
        [JsonPropertyName("heat")] [JsonConverter(typeof(FlexibleStringJsonConverter))] public string Heat { get; set; }
        [JsonPropertyName("score")]
        [JsonConverter(typeof(FlexibleNullableDoubleJsonConverter))]
        public double? Score { get; set; }
        [JsonPropertyName("postScores")] public PostScoreItem[] PostScores { get; set; }
        [JsonPropertyName("creator")] public PostCreator Creator { get; set; }
        [JsonPropertyName("postVersions")] public PostVersionItem[] PostVersions { get; set; }
        [JsonIgnore] public bool ShowLocalModInstallStatus { get; set; }
    }

    public class PostScoreItem
    {
        [JsonPropertyName("score")]
        [JsonConverter(typeof(FlexibleIntJsonConverter))]
        public int Score { get; set; }
    }

    public class PostCreator
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("nickname")] public string Nickname { get; set; }
        [JsonPropertyName("headImg")] public string HeadImg { get; set; }
    }

    public class PostVersionItem
    {
        private string m_version;

        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("postId")] public int PostId { get; set; }
        [JsonPropertyName("title")] public string Title { get; set; }
        [JsonPropertyName("version")]
        public string Version
        {
            get => m_version;
            set
            {
                m_version = value;
                NuGetVersion = ExternalVersionParser.ParseOrNull(value);
            }
        }
        [JsonIgnore] public NuGetVersion NuGetVersion { get; private set; }
        [JsonPropertyName("content")] public string Content { get; set; }
        [JsonPropertyName("gameVersionIds")] public int[] GameVersionIds { get; set; }
        [JsonPropertyName("fileType")]
        [JsonConverter(typeof(CommunityFileTypeJsonConverter))]
        public ScbbsPlusCommunityFileType FileType { get; set; }
        [JsonPropertyName("createdAt")] public string CreatedAt { get; set; }
        [JsonPropertyName("updatedAt")] public string UpdatedAt { get; set; }
        [JsonPropertyName("files")] public PostFileItem[] Files { get; set; }
        [JsonExtensionData] public Dictionary<string, JsonElement> ExtensionData { get; set; }
        [JsonIgnore] public bool ShowLocalModInstallStatus { get; set; }
    }

    public class PostFileItem
    {
        private string m_packageVersion;

        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("postId")] public int PostId { get; set; }
        [JsonPropertyName("filename")] public string Filename { get; set; }
        [JsonPropertyName("mimeType")] public string MimeType { get; set; }
        [JsonPropertyName("hash")] public string Hash { get; set; }
        [JsonPropertyName("url")] public string Url { get; set; }
        [JsonPropertyName("icon")] public string Icon { get; set; }
        [JsonPropertyName("type")]
        [JsonConverter(typeof(CommunityFileTypeJsonConverter))]
        public ScbbsPlusCommunityFileType Type { get; set; }
        [JsonPropertyName("size")]
        [JsonConverter(typeof(FlexibleStringJsonConverter))]
        public string Size { get; set; }
        [JsonPropertyName("status")] public int Status { get; set; }
        [JsonPropertyName("remark")] public string Remark { get; set; }
        [JsonPropertyName("downloadCount")] public int DownloadCount { get; set; }
        [JsonPropertyName("createdAt")] public string CreatedAt { get; set; }
        [JsonPropertyName("updatedAt")] public string UpdatedAt { get; set; }
        [JsonPropertyName("packageName")] public string PackageName { get; set; }
        [JsonPropertyName("packageVersion")]
        public string PackageVersion
        {
            get => m_packageVersion;
            set
            {
                m_packageVersion = value;
                NuGetVersion = ExternalVersionParser.ParseOrNull(value);
            }
        }
        [JsonIgnore] public NuGetVersion NuGetVersion { get; private set; }
        [JsonIgnore] public bool IsRepositoryReleaseAsset { get; set; }
    }

    public class UploadModsResponse : ApiEnvelope<UploadModsData>
    {
    }

    public class UploadModsData
    {
        [JsonPropertyName("list")] public PostFileItem[] List { get; set; }
        [JsonPropertyName("total")] public int Total { get; set; }
        [JsonPropertyName("page")] public int Page { get; set; }
        [JsonPropertyName("limit")] public int Limit { get; set; }
    }

    /// <summary>double? 版容错转换：服务端 score 类型不稳定，数字/字符串/其他一律归一化，解析失败归 null。</summary>
    public sealed class FlexibleNullableDoubleJsonConverter : JsonConverter<double?>
    {
        public override bool HandleNull => true;

        public override double? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                JsonTokenType.Number => reader.GetDouble(),
                JsonTokenType.String => double.TryParse(reader.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                    ? value
                    : null,
                _ => null
            };
        }

        public override void Write(Utf8JsonWriter writer, double? value, JsonSerializerOptions options)
        {
            if (value.HasValue)
            {
                writer.WriteNumberValue(value.Value);
            }
            else
            {
                writer.WriteNullValue();
            }
        }
    }

    /// <summary>int 版容错转换：服务端数值字段类型不稳定，数字/字符串归一化，解析失败归 0
    /// （消费方星级均值聚合已把 <1 视为无效项，0 即"排除该条"）。</summary>
    public sealed class FlexibleIntJsonConverter : JsonConverter<int>
    {
        public override bool HandleNull => true;

        public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                JsonTokenType.Number => reader.TryGetInt32(out int value) ? value : (int)Math.Round(reader.GetDouble()),
                JsonTokenType.String => int.TryParse(reader.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                    ? value
                    : 0,
                _ => 0
            };
        }

        public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
        {
            writer.WriteNumberValue(value);
        }
    }

    public sealed class FlexibleStringJsonConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                JsonTokenType.String => reader.GetString(),
                JsonTokenType.Number => reader.TryGetInt64(out long value)
                    ? value.ToString(CultureInfo.InvariantCulture)
                    : reader.GetDouble().ToString(CultureInfo.InvariantCulture),
                JsonTokenType.True => bool.TrueString,
                JsonTokenType.False => bool.FalseString,
                JsonTokenType.Null => null,
                _ => JsonDocument.ParseValue(ref reader).RootElement.ToString()
            };
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNullValue();
            }
            else
            {
                writer.WriteStringValue(value);
            }
        }
    }
}
