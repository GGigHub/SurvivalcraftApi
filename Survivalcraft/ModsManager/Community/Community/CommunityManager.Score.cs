using System;
using System.Threading;
using System.Threading.Tasks;

namespace Game
{
    // 评分提交：业务错误码与空响应都以异常上抛，由调用方决定提示方式。
    internal static partial class CommunityManager
    {
        public static async Task SubmitScoreAsync(
            CommunityApiClient apiClient,
            CommunityScoreRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            BasicApiResponse response = await apiClient.PostJsonAsync<BasicApiResponse>(
                $"{request.ApiBaseUrl}/post/score",
                request.Token,
                new
                {
                    postId = request.PostId,
                    score = Math.Clamp(request.Rating, 1, 5),
                    content = request.Content ?? string.Empty,
                    source = 1,
                    playTime = Math.Max(0L, (long)Math.Round(request.PlaySeconds)),
                    platform = request.Platform,
                    gameVersion = request.GameVersion
                },
                cancellationToken);
            if (response == null)
            {
                // 空响应不再被当提交成功，评分丢失必须让调用方感知。
                throw new InvalidOperationException(LanguageControl.Get("ScbbsPlusCommunityManager", "ScoreApiReturnedEmpty"));
            }
            if (response.Code != 0 && response.Code != 200)
            {
                throw new InvalidOperationException(response.Msg ?? LanguageControl.Get("ScbbsPlusCommunityManager", "ScoreApiReturnedMalformed"));
            }
        }
    }

    internal sealed class CommunityScoreRequest
    {
        public string ApiBaseUrl { get; set; }
        public string Token { get; set; }
        public int PostId { get; set; }
        public int Rating { get; set; }
        public string Content { get; set; }
        public double PlaySeconds { get; set; }
        public string Platform { get; set; }
        public string GameVersion { get; set; }
    }
}
