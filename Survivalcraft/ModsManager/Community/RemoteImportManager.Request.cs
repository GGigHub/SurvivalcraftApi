using System;
using Engine;

namespace Game
{
    // 远程导入请求构造：URL 校验、文件名安全提取与类型识别。
    public static partial class RemoteImportManager
    {
        public static RemoteImportRequest CreateRequest(string rawUrl, string preferredFileName = null)
        {
            string url = (rawUrl ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new InvalidOperationException(LanguageControl.Get("ScbbsPlusRemoteImportManager", "EnterResourceLink"));
            }
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException(LanguageControl.Get("ScbbsPlusRemoteImportManager", "InvalidUrl"));
            }

            // 先解码再取文件名：%2F%2E%2E 等转义必须先展开，否则 ".." 段会随文件名
            // 一路带入引擎 ImportMod 的 CombinePaths，构成远程链接任意路径写入。
            string fileName = string.IsNullOrWhiteSpace(preferredFileName)
                ? Storage.GetFileName(Uri.UnescapeDataString(uri.AbsolutePath ?? string.Empty))
                : Storage.GetFileName(preferredFileName.Trim());
            if (string.IsNullOrWhiteSpace(fileName)
                || fileName.IndexOf('/') >= 0
                || fileName.IndexOf('\\') >= 0
                || fileName == "."
                || fileName == "..")
            {
                throw new InvalidOperationException(LanguageControl.Get("ScbbsPlusRemoteImportManager", "CannotRecognizeFileName"));
            }

            ExternalContentType type = ResolveType(fileName);
            if (type == ExternalContentType.Unknown)
            {
                throw new InvalidOperationException(LanguageControl.Get("ScbbsPlusRemoteImportManager", "UnsupportedFileExtension"));
            }

            return new RemoteImportRequest
            {
                Url = uri.ToString(),
                FileName = fileName,
                Type = type
            };
        }

        internal static ExternalContentType ResolveType(string fileName)
        {
            ExternalContentType type = ExternalContentManager.ExtensionToType(
                Storage.GetExtensionBCL(fileName ?? string.Empty));
            return ExternalContentManager.IsEntryTypeDownloadSupported(type)
                ? type
                : ExternalContentType.Unknown;
        }
    }

    public sealed class RemoteImportRequest
    {
        public string Url { get; set; }
        public string FileName { get; set; }
        public ExternalContentType Type { get; set; }
    }
}
