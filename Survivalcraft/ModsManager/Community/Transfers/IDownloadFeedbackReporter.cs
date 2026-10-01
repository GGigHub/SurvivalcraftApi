namespace Game {
    interface IDownloadFeedbackReporter {
        Task ReportAsync(CommunityDownloadFeedbackInfo feedbackInfo, CancellationToken cancellationToken = default);
    }
}