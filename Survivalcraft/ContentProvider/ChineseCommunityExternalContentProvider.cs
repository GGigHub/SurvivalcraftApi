using System;
using System.IO;
using Engine;

namespace Game {
    /// <summary>
    /// 中文社区上传目标提供者：接新社区认证栈（AuthSessionManager，per-URL 会话 + 加密凭据文件），
    /// 取代已死的 SchubExternalContentProvider（旧 /com/files/* 云盘 + /com/api/login 登录族）。
    /// 登录/登出走 OpenAuthEntry 的交互式对话框；列举与分享链新接口暂无对应端点，
    /// 上传/下载/列举/链接四操作待社区发布屏（原 ScbbsPlusMyPublishedScreen）轮次接入新接口后补齐。
    /// </summary>
    public class ChineseCommunityExternalContentProvider : IExternalContentProvider {
        public string DisplayName => LanguageControl.Get("ContentScreen", "CommunityType", "ChineseCommunity");

        public string Description => IsLoggedIn
            ? LanguageControl.Get("ChineseCommunityExternalContentProvider", "LoggedIn")
            : LanguageControl.Get("ChineseCommunityExternalContentProvider", "NotLoggedIn");

        // 登录态恢复（EnsureStoredAuthLoaded）由全局帧泵每帧幂等驱动，此处纯读。
        public bool IsLoggedIn => AuthSessionManager.Instance.IsLoggedIn;

        public bool SupportsListing => false;

        public bool SupportsLinks => false;

        public bool RequiresLogin => true;

        public bool IsLocalProvider => false;

        // 新认证入口是交互式对话框（已登录→登出确认，未登录→登录框），进度与取消参数无用武之地。
        // 一次性订阅 LoginStateChanged 桥接回调：登录成功→success，用户关闭登录框→failure(null)（对齐旧 Schub 取消语义）。
        private bool m_waitingLoginState;
        private Action m_loginSuccess;
        private Action<Exception> m_loginFailure;

        public void Login(CancellableProgress progress, Action success, Action<Exception> failure) {
            AuthSessionManager.Instance.OpenAuthEntry();
            // 等待中重复调用（如 busy 框取消后重试）：换绑回调重新武装，保证本次调用的回调被接上。
            if (m_waitingLoginState) {
                AuthSessionManager.Instance.LoginStateChanged -= OnLoginStateChanged;
            }
            m_waitingLoginState = true;
            m_loginSuccess = success;
            m_loginFailure = failure;
            AuthSessionManager.Instance.LoginStateChanged += OnLoginStateChanged;
        }

        private void OnLoginStateChanged(AuthSessionManager.AuthChangeKind kind) {
            // 非终态（切服 LoggedOut、401 SessionExpired）不打断等待，继续等本次登录结果。
            if (kind != AuthSessionManager.AuthChangeKind.LoggedIn
                && kind != AuthSessionManager.AuthChangeKind.LoginCanceled) {
                return;
            }
            AuthSessionManager.Instance.LoginStateChanged -= OnLoginStateChanged;
            m_waitingLoginState = false;
            if (kind == AuthSessionManager.AuthChangeKind.LoggedIn) {
                m_loginSuccess?.Invoke();
            }
            else if (kind == AuthSessionManager.AuthChangeKind.LoginCanceled) {
                m_loginFailure?.Invoke(null);
            }
            m_loginSuccess = null;
            m_loginFailure = null;
        }

        // 新栈无静默登出入口：登录态下 OpenAuthEntry 弹登出确认框，确认后由 AuthSessionManager 登出。
        public void Logout()
            => AuthSessionManager.Instance.OpenAuthEntry();

        // 新社区接口已有对应能力（上传/文件元数据），但模组侧迁移未完成——
        // 待社区发布屏（原 ScbbsPlusMyPublishedScreen）轮次接入后补齐，先显式未实现。
        public void List(string path, CancellableProgress progress, Action<ExternalContentEntry> success, Action<Exception> failure)
            => throw new NotImplementedException();

        public void Download(string path, CancellableProgress progress, Action<Stream> success, Action<Exception> failure)
            => throw new NotImplementedException();

        public void Upload(string path, Stream stream, CancellableProgress progress, Action<string> success, Action<Exception> failure)
            => throw new NotImplementedException();

        public void Link(string path, CancellableProgress progress, Action<string> success, Action<Exception> failure)
            => throw new NotImplementedException();

        public void Dispose() {
            if (m_waitingLoginState) {
                AuthSessionManager.Instance.LoginStateChanged -= OnLoginStateChanged;
                m_waitingLoginState = false;
            }
            m_loginSuccess = null;
            m_loginFailure = null;
        }
    }
}
