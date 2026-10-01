using System;

namespace Game
{
    internal static class PostIconRules
    {
        public static string GetRemoteIconUrl(PostItem post, string apiBaseUrl)
        {
            if (post?.PostVersions != null)
            {
                foreach (PostVersionItem version in post.PostVersions)
                {
                    foreach (PostFileItem file in version?.Files ?? [])
                    {
                        if (!string.IsNullOrWhiteSpace(file?.Icon))
                        {
                            return NormalizeUrl(file.Icon, apiBaseUrl);
                        }
                    }
                }
            }
            return !string.IsNullOrWhiteSpace(post?.Icon)
                ? NormalizeUrl(post.Icon, apiBaseUrl)
                : !string.IsNullOrWhiteSpace(post?.Cover)
                    ? NormalizeUrl(post.Cover, apiBaseUrl)
                    : null;
        }

        public static string NormalizeUrl(string value, string apiBaseUrl)
        {
            string text = value?.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }
            if (Uri.TryCreate(text, UriKind.Absolute, out Uri absoluteUri))
            {
                return ApplyThumbnailSuffix(absoluteUri.ToString());
            }
            if (text.StartsWith("//", StringComparison.Ordinal))
            {
                return ApplyThumbnailSuffix("https:" + text);
            }
            if (Uri.TryCreate(apiBaseUrl, UriKind.Absolute, out Uri apiUri))
            {
                Uri rootUri = new($"{apiUri.Scheme}://{apiUri.Authority}");
                return ApplyThumbnailSuffix(new Uri(rootUri, text.StartsWith("/", StringComparison.Ordinal) ? text : "/" + text).ToString());
            }
            return ApplyThumbnailSuffix(text);
        }

        private static string ApplyThumbnailSuffix(string iconUrl)
        {
            const string suffix = "!/fwfh/368x238";
            if (!Uri.TryCreate(iconUrl, UriKind.Absolute, out Uri uri)
                || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(uri.Host, "img.suancaixianyu.cn", StringComparison.OrdinalIgnoreCase)
                || iconUrl.EndsWith(suffix, StringComparison.Ordinal))
            {
                return iconUrl;
            }
            return iconUrl + suffix;
        }
    }
}
