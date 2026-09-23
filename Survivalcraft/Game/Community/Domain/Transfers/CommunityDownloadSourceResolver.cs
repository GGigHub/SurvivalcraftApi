using System;

namespace Game
{
    internal static class CommunityDownloadSourceResolver
    {
        public static string ResolveUrl(string url, CommunityDownloadSourceOption source)
        {
            if (source == CommunityDownloadSourceOption.DefaultCn)
            {
                return url;
            }
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
            {
                return null;
            }

            UriBuilder builder = new(uri)
            {
                Host = source switch
                {
                    CommunityDownloadSourceOption.Source1Global => "r2.scbbs.top",
                    CommunityDownloadSourceOption.Source2Cn => "img.suancaixianyu.cn",
                    CommunityDownloadSourceOption.Source3Global => "r2img.ccxk.top",
                    CommunityDownloadSourceOption.Source4Global => "r2.ccxk.top",
                    _ => uri.Host
                }
            };
            return builder.Uri.ToString();
        }
    }
}
