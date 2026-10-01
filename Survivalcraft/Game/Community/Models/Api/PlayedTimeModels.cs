using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Game
{
    public class ModPlayRecordRequest
    {
        [JsonPropertyName("list")] public IReadOnlyList<ModPlayRecordItem> List { get; set; }
    }

    public class ModPlayRecordItem
    {
        [JsonPropertyName("packageName")] public string PackageName { get; set; }

        [JsonPropertyName("packageVersion")] public string PackageVersion { get; set; }

        // wire 键是服务器契约（camelCase），不随本地命名改动。
        [JsonPropertyName("playTime")] public int PlayedTime { get; set; }

        [JsonPropertyName("userId")] public int? UserId { get; set; }

        [JsonPropertyName("device")] public string Device { get; set; }

        [JsonPropertyName("playedAt")] public string PlayedAt { get; set; }
    }

    public class ModPlayRecordResponse : ApiEnvelope<ModPlayRecordResult>
    {
    }

    public class ModPlayRecordResult
    {
        [JsonPropertyName("recorded")] public int Recorded { get; set; }

        [JsonPropertyName("failed")] public List<ModPlayRecordFailure> Failed { get; set; }
    }

    public class ModPlayRecordFailure
    {
        [JsonPropertyName("reason")] public string Reason { get; set; }
    }
}
