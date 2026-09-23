using System;

namespace Game
{
    /// <summary>
    /// 传输基础设施静态域：下载器/上传/队列持久化/下载反馈/来源解析的唯一接线，
    /// 对齐游戏本体 *Manager 静态域类风格（原组合根成员提升为静态成员）。
    ///
    /// 顺序约束：字段按声明顺序初始化，后项构造可引用前项，勿反转。
    /// 反馈客户端的 token/baseUrl 提供者不能在静态初始化中接线（认证控制器在 Presentation 层，
    /// 本类不得反向引用），由组合根 CommunityApp.Initialize 经
    /// <see cref="ConfigureFeedbackProviders"/> 显式注入；接线前反馈按匿名上报（尽力而为语义不变）。
    /// </summary>
    internal static class TransferManager
    {
        private static Func<string> s_feedbackTokenProvider;
        private static Func<string> s_feedbackApiBaseUrlProvider;

        internal static readonly CommunityHttpDownloader CommunityHttpDownloader = new(HttpClientProvider.Community);
        internal static readonly CommunityUploadFileProvider UploadFileProvider = new();
        internal static readonly CommunityDownloadQueuePersistence CommunityDownloadQueuePersistence = new();
        internal static readonly CommunityDownloadFeedbackClient CommunityDownloadFeedbackClient = new(
            HttpClientProvider.Community,
            () => s_feedbackTokenProvider?.Invoke(),
            () => s_feedbackApiBaseUrlProvider?.Invoke());
        internal static readonly ModpackSourceResolver ModpackSourceResolver = new(
            HttpClientProvider.Community,
            CommunityManager.ApiClient);

        /// <summary>
        /// 组合根接线：注入下载反馈的登录态回退提供者（延迟求值，注入时不得调用——
        /// 认证控制器可能尚未初始化）。
        /// </summary>
        internal static void ConfigureFeedbackProviders(Func<string> tokenProvider, Func<string> apiBaseUrlProvider)
        {
            s_feedbackTokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
            s_feedbackApiBaseUrlProvider = apiBaseUrlProvider ?? throw new ArgumentNullException(nameof(apiBaseUrlProvider));
        }
    }
}
