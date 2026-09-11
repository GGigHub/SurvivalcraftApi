using System.Collections.Generic;

namespace Game
{
    internal sealed class LocalPublishedCheckRequest
    {
        public int RequestId { get; set; }
        public string ResourceKey { get; set; }
        public string DisplayName { get; set; }
        public string FileName { get; set; }
        public string PackageName { get; set; }
        public string ApiBaseUrl { get; set; }
        public string Token { get; set; }
    }

    internal sealed class LocalPublishedCheckResult
    {
        public int RequestId { get; set; }
        public string ResourceKey { get; set; }
        public string DisplayName { get; set; }
        public string FileName { get; set; }
        public int PostId { get; set; }
        public string Error { get; set; }
    }

    internal sealed class LocalResourceScoreRequest
    {
        public string ResourceKey { get; set; }
        public int CachedPostId { get; set; }
        public string FileName { get; set; }
        public string PackageName { get; set; }
        public double PlaySeconds { get; set; }
        public int Rating { get; set; }
        public string Content { get; set; }
        public string SuccessToastMessage { get; set; }
        public string ApiBaseUrl { get; set; }
        public string Token { get; set; }
        public string Platform { get; set; }
        public string GameVersion { get; set; }
    }

    internal sealed class LocalResourceScoreResult
    {
        public string ResourceKey { get; set; }
        public int PostId { get; set; }
        public int Rating { get; set; }
        public string Content { get; set; }
        public string SuccessToastMessage { get; set; }
        public string Error { get; set; }
    }
}
