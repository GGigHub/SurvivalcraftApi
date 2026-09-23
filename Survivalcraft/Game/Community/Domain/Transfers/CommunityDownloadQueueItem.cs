using System;
using System.Threading;

namespace Game
{
    internal sealed class CommunityDownloadQueueItem
    {
        public int Id { get; set; }
        public string Url { get; set; }
        public string OriginalUrl { get; set; }
        public string FileName { get; set; }
        public ExternalContentType Type { get; set; }
        public bool RequiresRestart { get; set; }
        public CommunityDownloadFeedbackInfo FeedbackInfo { get; set; }
        public bool ReplaceExistingScbbsPlusMod { get; set; }
        public string ReplaceExistingModPackageName { get; set; }
        public string ReplaceExistingModPath { get; set; }
        public string OverwriteExistingFilePath { get; set; }
        public string TempFilePath { get; set; }
        public CommunityTransferProgress Progress { get; set; }
        public CommunityDownloadSourceOption Source { get; set; }
        public CommunityDownloadQueueItemState State { get; set; }
        public DateTime CreatedTime { get; set; }
        public DateTime? StartedTime { get; set; }
        public DateTime? CompletedTime { get; set; }
        public CommunityFileDownloadResult Result { get; set; }
        public string Error { get; set; }
        public CancellationTokenSource CancelSource { get; set; }
        public bool PauseRequested { get; set; }

        /// <summary>
        ///     导入已成功（由工作流导入回调在后台线程当场置位并落盘）。进程在收尾前死亡时，
        ///     恢复流程据此把该项改判 Completed 终态，避免把已导入项当 Paused 再次导入。
        /// </summary>
        public bool Imported { get; set; }
    }
}
