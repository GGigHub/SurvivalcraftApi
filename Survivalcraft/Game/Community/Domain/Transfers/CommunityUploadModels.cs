namespace Game {
    enum CommunityUploadStage {
        Preparing,
        Exporting,
        RequestingCredentials,
        Uploading,
        Registering,
        Completed
    }

    sealed class CommunityUploadProgressInfo {
        public CommunityUploadStage Stage { get; init; }
        public float Value { get; init; }
        public string ResourceTypeName { get; init; }
    }

    public sealed class CommunityWorldUploadResult {
        public string Url { get; set; }
        public string FileName { get; set; }
        public long FileSize { get; set; }
        public string Error { get; set; }
        public bool Canceled { get; set; }
    }

    sealed class CommunityPreparedUploadFile {
        public string FilePath { get; init; }
        public string FileName { get; init; }
        public long FileSize { get; init; }
        public CommunityFileType FileType { get; init; }
        public bool IsTemporary { get; init; }
    }
}