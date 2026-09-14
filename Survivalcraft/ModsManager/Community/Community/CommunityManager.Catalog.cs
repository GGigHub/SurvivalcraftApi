using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Game
{
    // 游戏版本目录查询与插件版本匹配。
    internal static partial class CommunityManager
    {
        public static Task<VersionListResponse> LoadVersionCatalogAsync(
            CommunityApiClient apiClient,
            string apiBaseUrl,
            string token,
            CancellationToken cancellationToken = default)
        {
            return apiClient.GetJsonAsync<VersionListResponse>(
                $"{apiBaseUrl}/version/list",
                token,
                cancellationToken);
        }

        public static async Task<VersionListItem> ResolvePluginVersionAsync(
            CommunityApiClient apiClient,
            string apiBaseUrl,
            string token,
            string apiVersion,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(apiVersion))
            {
                return null;
            }

            CommunityApiResponse<VersionListResponse> apiResult =
                await LoadVersionCatalogResultAsync(apiClient, apiBaseUrl, token, cancellationToken);
            if (!apiResult.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusCommunityScreen", "Community", "Posts", "GetVersionListFailed"), apiResult.StatusCode, apiResult.Body));
            }
            if (apiResult.Value?.Code != 200 || apiResult.Value.Data == null)
            {
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusCommunityScreen", "Community", "Posts", "VersionListResponseMalformed"), apiResult.Body));
            }

            return CommunityModVersionSelector.FindBestMatch(apiResult.Value.Data, apiVersion);
        }

        private static Task<CommunityApiResponse<VersionListResponse>> LoadVersionCatalogResultAsync(
            CommunityApiClient apiClient,
            string apiBaseUrl,
            string token,
            CancellationToken cancellationToken = default)
        {
            return apiClient.GetJsonResultAsync<VersionListResponse>(
                $"{apiBaseUrl}/version/list",
                token,
                cancellationToken);
        }
    }
}
