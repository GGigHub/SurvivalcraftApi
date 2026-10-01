namespace Game {
    sealed class CommunityModCatalogClient {
        readonly CommunityApiClient m_apiClient;

        public CommunityModCatalogClient(CommunityApiClient apiClient) =>
            m_apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));

        public Task<UploadModsResponse> FindByPackageNameAsync(string apiBaseUrl,
            string token,
            string packageName,
            string versionRange = null,
            CancellationToken cancellationToken = default) {
            List<string> queryParts = [$"packageName={Uri.EscapeDataString(packageName ?? string.Empty)}", "types=5"];
            if (!string.IsNullOrWhiteSpace(versionRange)) {
                queryParts.Add($"versionRange={Uri.EscapeDataString(versionRange)}");
            }
            string baseUrl = CommunityServerManager.NormalizeApiBaseUrl(apiBaseUrl);
            return m_apiClient.GetJsonAsync<UploadModsResponse>($"{baseUrl}/upload/mods?{string.Join("&", queryParts)}", token, cancellationToken);
        }
    }
}