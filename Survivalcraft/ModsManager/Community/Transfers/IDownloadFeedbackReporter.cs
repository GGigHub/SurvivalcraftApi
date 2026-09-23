using System.Threading;
using System.Threading.Tasks;

namespace Game
{
    internal interface IDownloadFeedbackReporter
    {
        Task ReportAsync(CommunityDownloadFeedbackInfo feedbackInfo, CancellationToken cancellationToken = default);
    }
}
