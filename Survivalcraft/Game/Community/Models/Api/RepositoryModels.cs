using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Game
{
    public class GiteeReleaseResponse
    {
        [JsonPropertyName("tag_name")] public string TagName { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; }
        [JsonPropertyName("body")] public string Body { get; set; }
        [JsonPropertyName("assets")] public GiteeReleaseAsset[] Assets { get; set; }
    }

    public class GiteeReleaseAsset
    {
        [JsonPropertyName("name")] public string Name { get; set; }
        [JsonPropertyName("browser_download_url")] public string BrowserDownloadUrl { get; set; }
        [JsonExtensionData] public Dictionary<string, JsonElement> ExtensionData { get; set; }
    }
}
