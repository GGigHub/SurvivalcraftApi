using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Engine;

namespace Game
{
    // 首页精选资源：置顶榜与最近更新两路并行加载，单侧失败降级为空列表。
    internal static partial class CommunityManager
    {
        public static async Task<CommunityFeaturedData> LoadFeaturedAsync(
            CommunityApiClient apiClient,
            string apiBaseUrl,
            CancellationToken cancellationToken = default)
        {
            string topUrl = $"{apiBaseUrl}/public-stats/post-views/top?type=2&granularity=day&limit=6&format=post-list";
            string recentUrl = $"{apiBaseUrl}/public-stats/resources/recent-updates?format=post-list";
            // 两请求相互独立，先并行发出再逐项 await。单侧失败不拖垮另一侧（失败侧空列表，
            // 首屏高亮降级为部分内容）；两侧都失败保持整体抛异常——上层依赖该契约展示失败状态。
            Task<string> topTask = apiClient.GetStringAsync(topUrl, cancellationToken: cancellationToken);
            Task<string> recentTask = apiClient.GetStringAsync(recentUrl, cancellationToken: cancellationToken);
            string topBody = await AwaitOrNullAsync(topTask, cancellationToken);
            string recentBody = await AwaitOrNullAsync(recentTask, cancellationToken);
            if (topBody == null && recentBody == null)
            {
                // 两侧都失败：抛首个错误，维持上层「整体失败带 Error」的既有行为。
                throw new InvalidOperationException(topTask.Exception?.GetBaseException().Message
                    ?? recentTask.Exception?.GetBaseException().Message
                    ?? LanguageControl.Get("CommunityScreen", "FailedToLoadFeaturedResources"));
            }
            return new CommunityFeaturedData
            {
                TopResources = topBody == null ? [] : ParsePostList(topBody),
                RecentUpdates = recentBody == null ? [] : ParsePostList(recentBody)
            };
        }

        // 逐项 await：成功返回响应体，失败返回 null（用户取消传播）。两侧都失败时
        // 调用方依 topTask.Exception 取根错误重抛，维持整体失败契约。
        private static async Task<string> AwaitOrNullAsync(
            Task<string> task,
            CancellationToken cancellationToken)
        {
            try
            {
                return await task;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // 用户取消：传播，不吞成部分失败。
                throw;
            }
            catch (Exception ex)
            {
                Log.Warning(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("CommunityManager", "HighlightDataLoadFailed"), ex.Message));
                return null;
            }
        }

        private static IReadOnlyList<PostItem> ParsePostList(string body)
        {
            try
            {
                PostsResponse response = JsonSerializer.Deserialize<PostsResponse>(body);
                return response?.Code == 200 && response.Data?.List != null
                    ? response.Data.List
                    : [];
            }
            catch (JsonException)
            {
                return [];
            }
        }
    }

    internal sealed class CommunityFeaturedData
    {
        public IReadOnlyList<PostItem> TopResources { get; set; } = [];
        public IReadOnlyList<PostItem> RecentUpdates { get; set; } = [];
    }
}
