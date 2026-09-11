using System.Threading;
using System.Threading.Tasks;

namespace Game
{
    internal interface IDownloadFeedbackReporter
    {
        Task ReportAsync(DownloadFeedbackInfo feedbackInfo, CancellationToken cancellationToken = default);
    }
}
