using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Engine;

namespace Game
{
    internal sealed class DownloadFeedbackClient : IDownloadFeedbackReporter
    {
        private readonly HttpClient m_httpClient;
        private readonly Func<string> m_fallbackTokenProvider;
        private readonly Func<string> m_fallbackApiBaseUrlProvider;

        public DownloadFeedbackClient(
            HttpClient httpClient,
            Func<string> fallbackTokenProvider = null,
            Func<string> fallbackApiBaseUrlProvider = null)
        {
            m_httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            // 队列持久化剥离 Token（安全）：跨会话恢复项的快照为空，
            // 上报时经此回退按当前登录态补取；未登录返回 null → 匿名上报（尽力而为）。
            m_fallbackTokenProvider = fallbackTokenProvider;
            m_fallbackApiBaseUrlProvider = fallbackApiBaseUrlProvider;
        }

        public async Task ReportAsync(DownloadFeedbackInfo feedbackInfo, CancellationToken cancellationToken = default)
        {
            if (feedbackInfo == null)
            {
                return;
            }

            if (feedbackInfo.FileId <= 0 && feedbackInfo.PostId <= 0)
            {
                Log.Information(LanguageControl.Get("ScbbsPlusDownloadFeedbackClient", "MissingFeedbackIdsSkipped"));
                return;
            }

            string token = ResolveReportToken(feedbackInfo, m_fallbackTokenProvider, m_fallbackApiBaseUrlProvider);
            try
            {
                using HttpRequestMessage request = new(HttpMethod.Get, BuildRequestUrl(feedbackInfo));
                if (!string.IsNullOrWhiteSpace(token))
                {
                    request.Headers.Authorization =
                        new AuthenticationHeaderValue("Bearer", token);
                }

                using HttpResponseMessage response = await m_httpClient.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    if (UnauthorizedNotifier.IsUnauthorized(response.StatusCode)
                        && !string.IsNullOrWhiteSpace(token))
                    {
                        UnauthorizedNotifier.Notify(token);
                    }
                    string body = await response.Content.ReadAsStringAsync(cancellationToken);
                    Log.Warning(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadFeedbackClient", "RecordDownloadFeedbackFailed"), response.StatusCode, body));
                    return;
                }

                Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadFeedbackClient", "DownloadFeedbackRecorded"), feedbackInfo.PostId, feedbackInfo.FileId));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // 用户取消：传播给调用方；超时 OCE 不满足 when，仍按记录失败吞掉（反馈非关键路径）。
                throw;
            }
            catch (Exception ex)
            {
                Log.Warning(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadFeedbackClient", "RecordDownloadFeedbackFaulted"), ex.Message));
            }
        }

        internal static string ResolveReportToken(
            DownloadFeedbackInfo feedbackInfo,
            Func<string> fallbackTokenProvider,
            Func<string> fallbackApiBaseUrlProvider)
        {
            // 快照优先：还在会话内就用入队时的凭据（A 的令牌发 A，即使当前登录在 B 也正确）；
            // 为空（持久化剥离/跨会话恢复）才按当前登录态补取。登录态可能已换人，
            // 快照仍在时不得触碰 fallback。
            if (!string.IsNullOrWhiteSpace(feedbackInfo?.Token))
            {
                return feedbackInfo.Token;
            }
            try
            {
                string fallbackToken = fallbackTokenProvider?.Invoke();
                if (string.IsNullOrWhiteSpace(fallbackToken))
                {
                    return null;
                }
                // fallback 凭据只发往当前会话所属服务器：入队时在服务器 A、上报时登录在 B，
                // 把 B 的有效令牌发往 A = 凭据外泄给另一主机；A 回 401 还会携 B 的令牌
                // 触发误登出（审查缺陷 3 复审 F1）。服务器不一致（或当前服务器未知）→ 匿名上报。
                string currentApiBaseUrl = fallbackApiBaseUrlProvider?.Invoke();
                if (!string.Equals(feedbackInfo?.ApiBaseUrl, currentApiBaseUrl, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
                return fallbackToken;
            }
            catch (Exception ex)
            {
                // fallback 是外部扩展点：provider 异常按"无凭据"处理（匿名上报），
                // 不得把已成功导入的反馈上报炸成下载失败（复审 F2）。
                Log.Warning(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadFeedbackClient", "FeedbackAuthRefreshFailedReportingAnonymously"), ex.Message));
                return null;
            }
        }

        internal static string BuildRequestUrl(DownloadFeedbackInfo feedbackInfo)
        {
            List<string> queryParts =
            [
                "Action=feedback",
                "Feedback=Success",
                $"Platform={Uri.EscapeDataString(feedbackInfo.Platform ?? "Android")}"
            ];
            if (!string.IsNullOrWhiteSpace(feedbackInfo.Version))
            {
                queryParts.Add($"Version={Uri.EscapeDataString(feedbackInfo.Version)}");
            }
            if (feedbackInfo.PostId > 0)
            {
                queryParts.Add($"postId={feedbackInfo.PostId}");
            }
            if (feedbackInfo.FileId > 0)
            {
                queryParts.Add($"fileId={feedbackInfo.FileId}");
            }

            return $"{ApiUrls.NormalizeBaseUrl(feedbackInfo.ApiBaseUrl)}/download/record?{string.Join("&", queryParts)}";
        }
    }
}
