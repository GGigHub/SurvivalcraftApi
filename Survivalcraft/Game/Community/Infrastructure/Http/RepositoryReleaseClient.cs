using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Game
{
    internal sealed class RepositoryReleaseClient
    {
        private readonly HttpClient m_httpClient;

        public RepositoryReleaseClient(HttpClient httpClient)
        {
            m_httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        public async Task<GiteeReleaseResponse> FetchLatestAsync(
            string releaseApiUrl,
            string displayName,
            string providerName,
            CancellationToken cancellationToken = default)
        {
            using HttpRequestMessage request = new(HttpMethod.Get, releaseApiUrl);
            request.Headers.UserAgent.ParseAdd(HttpClientProvider.UserAgent);
            request.Headers.TryAddWithoutValidation("Accept", "application/json");

            using HttpResponseMessage response = await m_httpClient.SendAsync(request, cancellationToken);
            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusCommunityScreen", "Upload", "Update", "NoLatestRelease"), displayName));
            }
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusCommunityScreen", "Upload", "Update", "FetchReleaseFailed"), providerName, response.StatusCode));
            }

            // 空/非 JSON body（网关 200 空响应、HTML 错误页）归一化为 InvalidOperationException，
            // 不让裸 JsonException 逃到调用方（同 CommunityApiClient 的翻译约定）。
            if (string.IsNullOrWhiteSpace(body))
            {
                throw new InvalidOperationException($"{response.StatusCode}: <empty body>");
            }
            GiteeReleaseResponse release;
            try
            {
                release = JsonSerializer.Deserialize<GiteeReleaseResponse>(body);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"{response.StatusCode}: {ex.Message}", ex);
            }
            return release ?? throw new InvalidOperationException(LanguageControl.Get("ScbbsPlusCommunityScreen", "Upload", "Update", "ReleaseDataIsEmpty"));
        }
    }
}
