using System;
using System.Threading;
using System.Threading.Tasks;

namespace Game
{
    // 帖子详情装配：详情与下载趋势并行请求。
    internal static partial class CommunityManager
    {
        public static Task<CommunityApiResponse<PostDetailResponse>> GetPostDetailAsync(
            CommunityApiClient apiClient,
            string apiBaseUrl,
            string token,
            int postId,
            CancellationToken cancellationToken = default)
        {
            return apiClient.GetJsonResultAsync<PostDetailResponse>(
                $"{apiBaseUrl}/post/detail?id={postId}",
                token,
                cancellationToken);
        }

        public static async Task<CommunityPostDetailLoad> LoadPostDetailAsync(
            CommunityApiClient apiClient,
            string apiBaseUrl,
            string token,
            int postId,
            bool includeStats = false)
        {
            try
            {
                // 帖子详情与下载趋势相互独立，并行发出。
                Task<CommunityApiResponse<PostDetailResponse>> detailTask =
                    GetPostDetailAsync(apiClient, apiBaseUrl, token, postId);
                Task<(PostDownloadTrendResponse Stats, string Error)> statsTask = includeStats
                    ? GetPostDownloadTrendAsync(apiClient, apiBaseUrl, postId)
                    : Task.FromResult<(PostDownloadTrendResponse, string)>((null, null));
                await Task.WhenAll(detailTask, statsTask);
                CommunityApiResponse<PostDetailResponse> apiResult = detailTask.Result;
                (PostDownloadTrendResponse stats, string statsError) = statsTask.Result;

                return new CommunityPostDetailLoad
                {
                    Response = apiResult.Value,
                    Stats = stats,
                    StatsError = statsError,
                    Error = apiResult.ErrorSummary
                };
            }
            catch (Exception ex)
            {
                return new CommunityPostDetailLoad { Error = ex.Message };
            }
        }
    }

    internal sealed class CommunityPostDetailLoad
    {
        public PostDetailResponse Response { get; set; }
        public PostDownloadTrendResponse Stats { get; set; }
        public string StatsError { get; set; }
        public string Error { get; set; }
    }
}
