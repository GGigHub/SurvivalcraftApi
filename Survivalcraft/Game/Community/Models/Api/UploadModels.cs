using System.Text.Json;
using System.Text.Json.Serialization;

namespace Game {
    public sealed class UpyunCredentialsRequest {
        [JsonPropertyName("typeId")] public int TypeId { get; set; }

        [JsonPropertyName("saveKey")] public string SaveKey { get; set; }
    }

    public sealed class UpyunCredentialsResponse : ApiEnvelope<UpyunCredentials> { }

    public sealed class UpyunCredentials {
        [JsonPropertyName("policy")] public string Policy { get; set; }

        [JsonPropertyName("authorization")] public string Authorization { get; set; }

        [JsonPropertyName("uploadUrl")] public string UploadUrl { get; set; }
    }

    public sealed class UpyunUploadResponse {
        [JsonPropertyName("code")] public int Code { get; set; }

        [JsonPropertyName("url")] public string Url { get; set; }

        [JsonPropertyName("message")] public string Message { get; set; }
    }

    public sealed class UploadRegisterRequest {
        [JsonPropertyName("path")] public string Path { get; set; }

        [JsonPropertyName("type")] public int Type { get; set; }
    }

    public sealed class UploadRegisterResponse : ApiEnvelope<UploadFileData> { }

    public sealed class UploadFileData {
        [JsonPropertyName("id")] public int Id { get; set; }

        [JsonPropertyName("filename")] public string Filename { get; set; }

        [JsonPropertyName("size")] public JsonElement Size { get; set; }

        [JsonPropertyName("url")] public string Url { get; set; }
    }
}