using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Engine;

namespace Game
{
    /// <summary>
    /// 模组游玩时间上报客户端。上报属非关键路径：业务失败只记日志，不向调用方抛出。
    /// </summary>
    internal sealed class PlayedTimeClient
    {
        // null 字段（未登录的 userId、无版本号）不参与序列化，与服务端约定一致。
        private static readonly JsonSerializerOptions s_jsonOptions = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        // 测试经 InternalsVisibleTo 复用同一份选项，避免客户端改选项时测试仍绿而契约已变。
        internal static JsonSerializerOptions SerializationOptions => s_jsonOptions;

        private readonly HttpClient m_httpClient;

        public PlayedTimeClient(HttpClient httpClient)
        {
            m_httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        public async Task ReportAsync(
            string apiBaseUrl,
            string token,
            IReadOnlyList<ModPlayRecordItem> items,
            CancellationToken cancellationToken = default)
        {
            if (items == null || items.Count == 0)
            {
                return;
            }

            string url = BuildRequestUrl(apiBaseUrl);

            try
            {
                string body = JsonSerializer.Serialize(new ModPlayRecordRequest { List = items.ToArray() }, s_jsonOptions);
                using HttpRequestMessage request = new(HttpMethod.Post, url);
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                if (!string.IsNullOrWhiteSpace(token))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                }

                using HttpResponseMessage response = await m_httpClient.SendAsync(request, cancellationToken);
                // 故意不做 401/403 登出广播（不像 DownloadFeedbackClient）：上报随自动保存
                // 周期触发，token 过期时在游玩中反复弹「登录过期」属于打扰；仅记日志。
                string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                ModPlayRecordResponse result = JsonSerializer.Deserialize<ModPlayRecordResponse>(responseBody);
                if (!response.IsSuccessStatusCode || result?.Code != 200)
                {
                    string message = string.IsNullOrWhiteSpace(result?.Msg)
                        ? response.StatusCode.ToString()
                        : result.Msg;
                    throw new InvalidOperationException(message);
                }

                int failed = result.Data?.Failed?.Count ?? 0;
                Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusPlayedTimeClient", "PlayedTimeReportCompleted"), result.Data?.Recorded ?? 0, failed));
                if (failed > 0)
                {
                    string failureSummary = string.Join("; ", result.Data!.Failed!
                        .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Reason))
                        .Select(item => item.Reason));
                    Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusPlayedTimeClient", "PlayedTimeReportRejectedEntries"), failureSummary));
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Warning(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusPlayedTimeClient", "PlayedTimeReportFailed"), ex.Message));
            }
        }

        internal static string BuildRequestUrl(string apiBaseUrl)
        {
            // 缺省/空白基址回退默认服务器（NormalizeBaseUrl 保证），绝不拼出相对 URL。
            return $"{ApiUrls.NormalizeBaseUrl(apiBaseUrl)}/mod-play/record";
        }
    }
}
