namespace Game
{
    internal enum UploadStage
    {
        Preparing,
        Exporting,
        RequestingCredentials,
        Uploading,
        Registering,
        Completed
    }

    internal sealed class UploadProgressInfo
    {
        public UploadStage Stage { get; init; }
        public float Value { get; init; }
        public string ResourceTypeName { get; init; }
    }

    public sealed class WorldUploadResult
    {
        public string Url { get; set; }
        public string FileName { get; set; }
        public long FileSize { get; set; }
        public string Error { get; set; }
        public bool Canceled { get; set; }
    }

    internal sealed class PreparedUploadFile
    {
        public string FilePath { get; init; }
        public string FileName { get; init; }
        public long FileSize { get; init; }
        public ScbbsPlusCommunityFileType FileType { get; init; }
        public bool IsTemporary { get; init; }
    }
}
