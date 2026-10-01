namespace Game {
    static class CommunityDownloadSourceResolver {
        public static string ResolveUrl(string url, CommunityDownloadSourceManager.Info source) {
            // Host 为空 = 默认线路，原站直连不改写
            string host = source?.Host?.Trim();
            if (string.IsNullOrEmpty(host)) {
                return url;
            }
            if (string.IsNullOrWhiteSpace(url)
                || !Uri.TryCreate(url, UriKind.Absolute, out Uri uri)) {
                return null;
            }

            // 主机来自用户输入：非法字符/畸形端口会让 UriBuilder.Uri 抛 UriFormatException，
            // 与「无法切换」对话框的空结果契约对齐，按 null 处理而非裸抛
            try {
                int mirrorPort = -1;
                UriBuilder builder = new(uri) { Host = NormalizeHost(host, out mirrorPort) };
                if (mirrorPort >= 0) {
                    builder.Port = mirrorPort;
                }
                return builder.Uri.ToString();
            }
            catch (UriFormatException) {
                return null;
            }
        }

        // 容错：镜像主机填成完整 URL 时取其主机部分并回传显式端口（-1 = 保留原 URL 端口），
        // 裸域名原样使用
        static string NormalizeHost(string host, out int port) {
            port = -1;
            if (Uri.TryCreate(host, UriKind.Absolute, out Uri parsed)
                && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps)) {
                if (!parsed.IsDefaultPort) {
                    port = parsed.Port;
                }
                return parsed.Host;
            }
            return host;
        }
    }
}