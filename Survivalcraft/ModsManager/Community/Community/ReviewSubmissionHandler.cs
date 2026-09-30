using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Engine;

namespace Game
{
    /// <summary>评分提交请求：本地屏（存档/模组/家具等）与社区屏（自评分）共用同一形状。</summary>
    internal sealed class ReviewSubmissionRequest
    {
        public string ResourceKey { get; set; }
        public int CachedPostId { get; set; }
        public string FileName { get; set; }
        public double PlaySeconds { get; set; }
        public int Rating { get; set; }
        public string Content { get; set; }
        public string PackageName { get; set; }
        public string SuccessToastMessage { get; set; }
        public bool ShowSubmittingDialog { get; set; } = true;
    }

    /// <summary>
    /// 评分提交管线：busy/未登录守卫（含对话框）、后台提交、结果落地（评价缓存写入 +
    /// 成功 toast/对话框 + 失败对话框）。本地屏多一层"结果是否仍属当前选中"的校验，
    /// 经 isCurrent 注入；社区屏不校验。
    /// </summary>
    internal sealed class ReviewSubmissionHandler
    {
        private readonly TaskSlot<LocalResourceScoreResult> m_slot = new(LanguageControl.Get("TaskSlot", "ScoreSubmission"));
        private readonly Action<string, double> m_showToast;

        /// <summary>会话守卫口：组合根默认读 AuthSessionController；测试注入替身。</summary>
        internal Func<bool> IsLoggedInProvider { get; set; } = () => AuthSessionManager.Instance.IsLoggedIn;

        /// <summary>提交凭据口：提交瞬间取快照，与守卫读数同源。</summary>
        internal Func<string> TokenProvider { get; set; } = () => AuthSessionManager.Instance.CurrentToken;

        internal Func<string> ApiBaseUrlProvider { get; set; } = () => AuthSessionManager.Instance.ApiBaseUrl;

        /// <summary>提交执行口：默认走 SharedServices 工作流；测试注入桩。</summary>
        internal Func<LocalResourceScoreRequest, Task<LocalResourceScoreResult>> SubmitProvider { get; set; }
            = request => SubmitScoreAsync(LocalModPostResolver.Instance, CommunityManager.ApiClient, request);

        /// <param name="showToast">toast 展示入口；两屏的 ShowToastPrompt(message, autoHideSeconds) 签名一致。</param>
        public ReviewSubmissionHandler(Action<string, double> showToast)
        {
            m_showToast = showToast ?? throw new ArgumentNullException(nameof(showToast));
        }

        /// <summary>守卫不通过时弹对应对话框并返回 false；接受时启动提交并按需弹提交中提示。</summary>
        public bool TrySubmit(ReviewSubmissionRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            if (m_slot.IsRunning)
            {
                DialogsManager.ShowDialog(null, new MessageDialog(LanguageControl.Get("ReviewSubmissionHandler", "PleaseWait"), LanguageControl.Get("ReviewSubmissionHandler", "SubmittingPreviousReview"), LanguageControl.Ok, null, null));
                return false;
            }
            if (!IsLoggedInProvider() || string.IsNullOrEmpty(TokenProvider()))
            {
                DialogsManager.ShowDialog(null, new MessageDialog(LanguageControl.Error, LanguageControl.Get("ReviewSubmissionHandler", "PleaseLogInBeforeReviewing"), LanguageControl.Ok, null, null));
                return false;
            }

            string token = TokenProvider();
            string apiBaseUrl = ApiBaseUrlProvider();
            Func<LocalResourceScoreRequest, Task<LocalResourceScoreResult>> submit = SubmitProvider;
            m_slot.Start((_, _) => submit(
                new LocalResourceScoreRequest
                {
                    ResourceKey = request.ResourceKey,
                    CachedPostId = request.CachedPostId,
                    FileName = request.FileName,
                    PackageName = request.PackageName,
                    PlaySeconds = request.PlaySeconds,
                    Rating = request.Rating,
                    Content = request.Content,
                    SuccessToastMessage = request.SuccessToastMessage,
                    ApiBaseUrl = apiBaseUrl,
                    Token = token,
                    Platform = DownloadFlowManager.GetPlatformName(),
                    GameVersion = VersionCatalogCache.CurrentApiVersion
                }));
            if (request.ShowSubmittingDialog)
            {
                DialogsManager.ShowDialog(null, new MessageDialog(LanguageControl.Get("ReviewSubmissionHandler", "Submitting"), LanguageControl.Get("ReviewSubmissionHandler", "SubmittingReviewToTheCommunity"), LanguageControl.Ok, null, null));
            }
            return true;
        }

        /// <summary>
        /// 消费完成结果并落地。返回 true 表示已消费（无论是否为陈旧/失败结果）。
        /// isCurrent 为假的结果静默丢弃（不写缓存不弹窗），与历史行为一致。
        /// </summary>
        public bool TryTakeResult(Func<LocalResourceScoreResult, bool> isCurrent = null)
        {
            if (!m_slot.TryTake(out LocalResourceScoreResult result, ex =>
                {
                    // 任务 Faulted 不冒泡出 Update()，带 Error 的 fallback 走既有失败对话框，
                    // 让用户知道提交失败可重试，而不是无声丢弃。
                    Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ReviewSubmissionHandler", "ScoreSubmissionTaskFaulted"), ex.Message));
                    return new LocalResourceScoreResult { Error = ex.Message };
                }))
            {
                return false;
            }
            if (result == null || isCurrent?.Invoke(result) == false)
            {
                return true;
            }
            if (!string.IsNullOrEmpty(result.Error))
            {
                DialogsManager.ShowDialog(null, new MessageDialog(LanguageControl.Error, string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ReviewSubmissionHandler", "ReviewSubmissionFailed"), result.Error), LanguageControl.Ok, null, null));
                return true;
            }

            SaveReviewEntry(result.ResourceKey, result.PostId, result.Rating, result.Content);
            if (!string.IsNullOrWhiteSpace(result.SuccessToastMessage))
            {
                m_showToast(result.SuccessToastMessage, 2.0);
            }
            else
            {
                DialogsManager.ShowDialog(null, new MessageDialog(LanguageControl.Get("ReviewSubmissionHandler", "ReviewSubmitted"), LanguageControl.Get("ReviewSubmissionHandler", "ReviewSubmittedToast"), LanguageControl.Ok, null, null));
            }
            return true;
        }

        private static void SaveReviewEntry(string key, int postId, int rating, string text)
        {
            if (string.IsNullOrEmpty(key))
            {
                return;
            }
            text ??= string.Empty;
            LocalModStateStore.LocalModReviews[key] = new LocalModReviewEntry
            {
                PostId = postId,
                Rating = Math.Clamp(rating, 1, 5),
                Text = text,
                LastText = text,
                UpdatedAt = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
            };
            LocalModStateStore.SaveLocalModReviews();
        }

        /// <summary>
        /// 提交评分：缓存 postId 命中则跳过解析，否则按文件名/包名解析；解析不到
        /// 已发布帖子返回带文件名的 Error 结果。resolver 与 api 客户端经参数穿线
        /// （生产传组合根静态字段，测试传 HTTP 桩）。
        /// </summary>
        internal static async Task<LocalResourceScoreResult> SubmitScoreAsync(
            LocalModPostResolver postResolver,
            CommunityApiClient apiClient,
            LocalResourceScoreRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            try
            {
                int postId = request.CachedPostId > 0
                    ? request.CachedPostId
                    : await postResolver.ResolvePostIdAsync(
                        request.ApiBaseUrl,
                        request.Token,
                        request.FileName,
                        request.PackageName,
                        cancellationToken);
                if (postId <= 0)
                {
                    return new LocalResourceScoreResult
                    {
                        ResourceKey = request.ResourceKey,
                        Error = string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("LocalModNetworkManager", "CannotFindPublishedPostByFileName"), request.FileName)
                    };
                }

                await CommunityManager.SubmitScoreAsync(apiClient, new CommunityScoreRequest
                {
                    ApiBaseUrl = request.ApiBaseUrl,
                    Token = request.Token,
                    PostId = postId,
                    Rating = request.Rating,
                    Content = request.Content,
                    PlaySeconds = request.PlaySeconds,
                    Platform = request.Platform,
                    GameVersion = request.GameVersion
                }, cancellationToken);
                return new LocalResourceScoreResult
                {
                    ResourceKey = request.ResourceKey,
                    PostId = postId,
                    Rating = request.Rating,
                    Content = request.Content ?? string.Empty,
                    SuccessToastMessage = request.SuccessToastMessage
                };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // 用户取消：传播，不上报为查询错误。
                throw;
            }
            catch (Exception ex)
            {
                return new LocalResourceScoreResult
                {
                    ResourceKey = request.ResourceKey,
                    Error = ex.Message
                };
            }
        }
    }
}
