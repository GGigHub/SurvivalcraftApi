namespace Game
{
    public sealed class CommunityDownloadFeedbackInfo
    {
        public string ApiBaseUrl { get; set; }
        public string Token { get; set; }
        public int PostId { get; set; }
        public int FileId { get; set; }
        public string Platform { get; set; }
        public string Version { get; set; }
    }
}
