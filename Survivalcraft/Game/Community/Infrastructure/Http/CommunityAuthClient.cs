using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Engine;

namespace Game {
    sealed class CommunityAuthClient {
        readonly HttpClient m_httpClient;

        public CommunityAuthClient(HttpClient httpClient) => m_httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

        public async Task<LoginResponse> LoginAsync(string apiBaseUrl,
            string account,
            string password,
            CancellationToken cancellationToken = default) {
            try {
                string json = JsonSerializer.Serialize(new { account, password });
                using StringContent content = new(json, Encoding.UTF8, "application/json");
                using HttpResponseMessage response = await m_httpClient.PostAsync($"{apiBaseUrl}/user/login", content, cancellationToken);
                string body = await response.Content.ReadAsStringAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(body)) {
                    return new LoginResponse { Code = -1, Msg = LanguageControl.Get("LoginDialog", "LoginResponseIsEmpty") };
                }
                LoginResponse result = JsonSerializer.Deserialize<LoginResponse>(body);
                // 账号/密码错误由服务端响应码承载（如 Code=401），此处区分网络故障与响应异常，
                // 避免把非 JSON 错误页当成网络错误误导用户。
                return result ?? new LoginResponse { Code = -1, Msg = LanguageControl.Get("LoginDialog", "LoginResponseIsEmpty") };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                // 用户取消：传播，不吞成"网络错误"。HttpClient.Timeout 抛的 OCE 不满足 when（CT 未请求取消），
                // 仍落通用 catch 返回错误码，保持超时契约。
                throw;
            }
            catch (HttpRequestException ex) {
                Log.Error(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("AuthClient", "LoginRequestFailed"), ex.Message));
                return new LoginResponse {
                    Code = -1, Msg = string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("LoginDialog", "LoginNetworkError"), ex.Message)
                };
            }
            catch (JsonException ex) {
                Log.Error(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("AuthClient", "LoginResponseMalformed"), ex.Message));
                return new LoginResponse {
                    Code = -1,
                    Msg = string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("LoginDialog", "LoginResponseFormatError"), ex.Message)
                };
            }
            catch (Exception ex) {
                Log.Error(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("AuthClient", "LoginRequestFailed"), ex.Message));
                return new LoginResponse {
                    Code = -1, Msg = string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("LoginDialog", "LoginNetworkError"), ex.Message)
                };
            }
        }

        public async Task<bool> LogoutAsync(string apiBaseUrl, string token, CancellationToken cancellationToken = default) {
            try {
                using HttpRequestMessage request = new(HttpMethod.Post, $"{apiBaseUrl}/user/logout");
                if (!string.IsNullOrWhiteSpace(token)) {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                }
                using HttpResponseMessage response = await m_httpClient.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode
                    && !string.IsNullOrWhiteSpace(token)
                    && CommunityUnauthorizedNotifier.IsUnauthorized(response.StatusCode)) {
                    // 登出时 token 可能已失效：广播无害（订阅方只处理 LoggedIn 状态），保持一致性。
                    CommunityUnauthorizedNotifier.Notify(token);
                }
                Log.Information(
                    string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("AuthClient", "LogoutRequestCompleted"), response.StatusCode)
                );
                return response.IsSuccessStatusCode;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                // 用户取消：传播；超时 OCE 不满足 when，仍返回 false。
                throw;
            }
            catch (Exception ex) {
                Log.Error(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("AuthClient", "LogoutRequestFailed"), ex.Message));
                return false;
            }
        }
    }
}