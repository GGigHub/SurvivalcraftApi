using System.Globalization;

namespace Game {
    // 游戏版本目录查询与插件版本匹配。
    static partial class CommunityManager {
        public static Task<VersionListResponse> LoadVersionCatalogAsync(CommunityApiClient apiClient,
            string apiBaseUrl,
            string token,
            CancellationToken cancellationToken = default) => apiClient.GetJsonAsync<VersionListResponse>(
            $"{apiBaseUrl}/version/list",
            token,
            cancellationToken
        );

        public static async Task<VersionListItem> ResolveApiVersionAsync(CommunityApiClient apiClient,
            string apiBaseUrl,
            string token,
            string apiVersion,
            CancellationToken cancellationToken = default) {
            if (string.IsNullOrWhiteSpace(apiVersion)) {
                return null;
            }
            CommunityApiResponse<VersionListResponse> apiResult = await LoadVersionCatalogResultAsync(
                apiClient,
                apiBaseUrl,
                token,
                cancellationToken
            );
            if (!apiResult.IsSuccessStatusCode) {
                throw new InvalidOperationException(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        LanguageControl.Get("CommunityManager", "GetVersionListFailed"),
                        apiResult.StatusCode,
                        apiResult.Body
                    )
                );
            }
            if (apiResult.Value?.Code != 200
                || apiResult.Value.Data == null) {
                throw new InvalidOperationException(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        LanguageControl.Get("CommunityManager", "VersionListResponseMalformed"),
                        apiResult.Body
                    )
                );
            }
            return CommunityModVersionSelector.FindBestMatch(apiResult.Value.Data, apiVersion);
        }

        static Task<CommunityApiResponse<VersionListResponse>> LoadVersionCatalogResultAsync(CommunityApiClient apiClient,
            string apiBaseUrl,
            string token,
            CancellationToken cancellationToken = default) => apiClient.GetJsonResultAsync<VersionListResponse>(
            $"{apiBaseUrl}/version/list",
            token,
            cancellationToken
        );
    }
}