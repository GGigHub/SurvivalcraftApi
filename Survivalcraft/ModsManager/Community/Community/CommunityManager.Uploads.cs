namespace Game {
    // 上传文件列表查询：我的发布屏"只看未发布"开关直查 /upload/list（token 归属"我的上传"，
    // 与帖子解耦——未发正式帖子的资源也可见）。
    static partial class CommunityManager {
        // orderType=4（热门）与 Web 端默认一致，固定值；name 恒空（本屏无搜索框），
        // 保留参数对齐 Web 请求。types 直接用 CommunityFileType 整数值。
        // publishState：0 全部、1 已发布、2 未发布；本屏开关只查未发布，固定 2。
        public static async Task<CommunityUploadFilesLoad> LoadMyUploadsAsync(CommunityApiClient apiClient,
            string apiBaseUrl,
            string token,
            int page,
            int limit,
            CommunityFileType fileType,
            CancellationToken cancellationToken = default) {
            try {
                CommunityApiResponse<UploadModsResponse> apiResult = await apiClient.GetJsonResultAsync<UploadModsResponse>(
                    $"{apiBaseUrl}/upload/list?types={(int)fileType}&page={Math.Max(page, 1)}&limit={limit}&name=&orderType=4&publishState=2",
                    token,
                    cancellationToken
                );
                return new CommunityUploadFilesLoad { Response = apiResult.Value, Error = apiResult.ErrorSummary };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                throw;
            }
            catch (Exception ex) {
                return new CommunityUploadFilesLoad { Error = ex.Message };
            }
        }
    }

    sealed class CommunityUploadFilesLoad {
        public UploadModsResponse Response { get; set; }
        public string Error { get; set; }
    }
}