using System;
using System.Globalization;
using System.Net;
using Engine;

namespace Game
{
    /// <summary>
    /// 平台 API 401/403 响应广播。token 失效后任一带 Bearer 的请求失败都会通知，
    /// 由 ScbbsPlusCommunityScreen 统一清除本地登录态并提示重新登录，各调用点无需逐个处理。
    /// 通知携带触发请求的 token：订阅方比对当前凭据，旧 token 的在途请求不会误杀新会话。
    /// </summary>
    internal static class UnauthorizedNotifier
    {
        public static event Action<string> UnauthorizedResponse;

        public static void Notify(string token)
        {
            // 逐订阅者隔离：订阅方做 UI 编组可能抛异常，不得让它逃进 HTTP 调用路径
            // 替换掉真正的状态码错误。
            Action<string> handlers = UnauthorizedResponse;
            if (handlers == null)
            {
                return;
            }
            foreach (object invocation in handlers.GetInvocationList())
            {
                try
                {
                    ((Action<string>)invocation)(token);
                }
                catch (Exception ex)
                {
                    Log.Information(string.Format(CultureInfo.InvariantCulture, "[ScbbsPlus] Unauthorized notifier subscriber failed: {0}", ex.Message));
                }
            }
        }

        public static bool IsUnauthorized(HttpStatusCode statusCode)
        {
            return statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
        }
    }
}
