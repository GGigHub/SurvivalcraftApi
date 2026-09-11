using System;
using System.Net.Http;
using System.Threading;

namespace Game
{
    /// <summary>
    /// 集中创建共享 HTTP 客户端，同时保留社区请求与远程导入的超时策略差异。
    /// </summary>
    internal static class HttpClientProvider
    {
        // 声明顺序即静态初始化顺序：Create 引用 UserAgent，必须先于三个客户端声明。
        internal static string UserAgent { get; } = $"ScbbsPlus/{VersionsManager.Version}";

        public static HttpClient Community { get; } = Create(TimeSpan.FromSeconds(30));

        public static HttpClient RemoteImport { get; } = Create(TimeSpan.FromSeconds(100));

        // 上传专用：HttpClient.Timeout 约束覆盖整个请求体发送阶段（ProgressStreamContent
        // 与超时 CTS 链接），大文件上传用 30s 客户端在常见带宽下必超时。上传工作流
        // 全程透传用户取消令牌（凭证/上传/登记三处 HTTP 调用均透传），故此处不设
        // 超时、交由取消令牌兜底；凭证与登记小请求在工作流内另挂 30s CancelAfter。
        public static HttpClient Upload { get; } = Create(Timeout.InfiniteTimeSpan);

        private static HttpClient Create(TimeSpan timeout)
        {
            HttpClient client = new()
            {
                Timeout = timeout
            };
            // 版本取游戏程序集版本（VersionsManager.Version；mod 原取自身 AssemblyInfo，游戏本体无 modinfo 版本——批准的最小适配点）：
            // 硬编码会随版本演进漂移（曾钉死 1.0 而实际已 2.0，主报告 #9）。
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
            return client;
        }
    }
}
