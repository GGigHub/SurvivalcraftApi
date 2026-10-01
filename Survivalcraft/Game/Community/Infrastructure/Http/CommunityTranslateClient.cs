using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Game {
    /// <summary>
    /// 社区 /translate 接口封装。服务端最终由 AI 大模型承接，约定直接投递原文
    /// （整篇帖子 HTML 或标题等纯文本），由服务端保结构翻译并缓存相同内容的结果。
    /// </summary>
    sealed class CommunityTranslateClient {
        public static readonly CommunityTranslateClient Shared = new(CreateHttpClient());

        // 服务端响应键大小写未见书面约定（历史实现按 PascalCase 绑定），大小写不敏感兜住两种风格。
        static readonly JsonSerializerOptions s_responseOptions = new() { PropertyNameCaseInsensitive = true };

        readonly HttpClient m_httpClient;

        public CommunityTranslateClient(HttpClient httpClient) {
            m_httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            // 追加共享 UA（含程序集版本），不覆盖客户端既有 UA。
            m_httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(HttpClientProvider.UserAgent);
        }

        // 空白内容直接原样返回；翻译结果空白也回退原文（服务端可能对无意义输入返回空串）。
        public async Task<string> TranslateAsync(string translateUrl, string content, string targetLanguage, CancellationToken cancellationToken) {
            if (string.IsNullOrWhiteSpace(content)) {
                return content;
            }
            string requestBody = JsonSerializer.Serialize(new TranslateRequest { Content = content, TargetLanguage = targetLanguage });
            using HttpRequestMessage request = new(HttpMethod.Post, translateUrl) {
                Content = new StringContent(requestBody, Encoding.UTF8, "application/json")
            };
            using HttpResponseMessage response = await m_httpClient.SendAsync(request, cancellationToken);
            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode) {
                throw new InvalidOperationException($"{response.StatusCode}: {responseBody}");
            }
            TranslateResponse result = JsonSerializer.Deserialize<TranslateResponse>(responseBody, s_responseOptions);
            if (result?.Code != 200) {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(result?.Msg) ? responseBody : result.Msg);
            }
            string translatedText = result.Data?.TranslatedText;
            return string.IsNullOrWhiteSpace(translatedText) ? content : translatedText;
        }

        static HttpClient CreateHttpClient() => new();

        sealed class TranslateRequest {
            // 服务端（Nest 风格校验）只认 camelCase 键，多余/错名属性直接 400 拒收。
            [JsonPropertyName("content")] public string Content { get; set; }
            [JsonPropertyName("targetLanguage")] public string TargetLanguage { get; set; }
        }

        sealed class TranslateResponse {
            public int Code { get; set; }
            public string Msg { get; set; }
            public TranslateData Data { get; set; }
        }

        sealed class TranslateData {
            public string TranslatedText { get; set; }
        }
    }
}