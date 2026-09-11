using System;
using System.Text.Json;

namespace Game
{
    internal static class RepositoryReleaseRules
    {
        public static GiteeReleaseAsset FindScmodAsset(GiteeReleaseResponse release)
        {
            if (release?.Assets == null)
            {
                return null;
            }

            foreach (GiteeReleaseAsset asset in release.Assets)
            {
                if (asset == null)
                {
                    continue;
                }
                string name = asset.Name ?? string.Empty;
                string url = GetDownloadUrl(asset);
                if (name.EndsWith(".scmod", StringComparison.OrdinalIgnoreCase)
                    || url.EndsWith(".scmod", StringComparison.OrdinalIgnoreCase))
                {
                    return asset;
                }
            }
            return null;
        }

        public static string GetDownloadUrl(GiteeReleaseAsset asset)
        {
            if (!string.IsNullOrWhiteSpace(asset?.BrowserDownloadUrl))
            {
                return asset.BrowserDownloadUrl;
            }
            if (asset?.ExtensionData == null)
            {
                return string.Empty;
            }

            string[] keys = ["download_url", "url", "direct_asset_url"];
            foreach (string key in keys)
            {
                if (asset.ExtensionData.TryGetValue(key, out JsonElement element)
                    && element.ValueKind == JsonValueKind.String)
                {
                    string value = element.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }
            }
            return string.Empty;
        }
    }
}
