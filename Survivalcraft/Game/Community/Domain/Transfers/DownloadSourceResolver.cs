using System;

namespace Game
{
    internal static class DownloadSourceResolver
    {
        public static string ResolveUrl(string url, DownloadSourceOption source)
        {
            if (source == DownloadSourceOption.DefaultCn)
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
                    DownloadSourceOption.Source1Global => "r2.scbbs.top",
                    DownloadSourceOption.Source2Cn => "img.suancaixianyu.cn",
                    DownloadSourceOption.Source3Global => "r2img.ccxk.top",
                    DownloadSourceOption.Source4Global => "r2.ccxk.top",
                    _ => uri.Host
                }
            };
            return builder.Uri.ToString();
        }
    }
}
