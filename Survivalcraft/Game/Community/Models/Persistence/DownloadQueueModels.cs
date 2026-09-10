using System;
using System.Collections.Generic;

namespace Game
{
    public sealed class DownloadQueuePersistedState
    {
        public int Version { get; set; }
        public int NextId { get; set; }
        public IReadOnlyList<DownloadQueueStoredItem> Items { get; set; }
    }

    public sealed class DownloadQueueStoredItem
    {
        public int Id { get; set; }
        public string Url { get; set; }
        public string OriginalUrl { get; set; }
        public string FileName { get; set; }
        public ExternalContentType Type { get; set; }
        public bool RequiresRestart { get; set; }
        public DownloadFeedbackInfo FeedbackInfo { get; set; }
        public bool ReplaceExistingScbbsPlusMod { get; set; }
        public string ReplaceExistingModPackageName { get; set; }
        public string ReplaceExistingModPath { get; set; }
        public string OverwriteExistingFilePath { get; set; }
        public string TempFilePath { get; set; }
        public DownloadSourceOption Source { get; set; }
        public DownloadQueueItemState State { get; set; }
        public DateTime CreatedTime { get; set; }
        public DateTime? StartedTime { get; set; }
        public DateTime? CompletedTime { get; set; }
        public string Error { get; set; }

        /// <summary>导入已成功（由导入回调即时落盘）；恢复时据此改判 Completed 终态。</summary>
        public bool Imported { get; set; }
    }
}
