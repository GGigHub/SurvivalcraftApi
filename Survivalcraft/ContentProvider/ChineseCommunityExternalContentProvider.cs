using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Engine;

namespace Game {
    /// <summary>
    /// 中文社区上传目标提供者：接新社区认证栈（AuthSessionManager，per-URL 会话 + 加密凭据文件），
    /// 取代已死的 SchubExternalContentProvider（旧 /com/files/* 云盘 + /com/api/login 登录族）。
    /// 登录/登出走 OpenAuthEntry 的交互式对话框；列举跳转专用屏（CommunityMyPublishedScreen，
    /// 帖子/分页形状无文件夹语义）；上传走又拍云直传+登记链（CommunityUploadClient）；
    /// List/Download/Link 新接口无 provider 侧对应面，保留未实现。
    /// </summary>
    public class ChineseCommunityExternalContentProvider : IExternalContentProvider {
        public string DisplayName => LanguageControl.Get("ContentScreen", "CommunityType", "ChineseCommunity");

        public string Description => IsLoggedIn
            ? LanguageControl.Get("ChineseCommunityExternalContentProvider", "LoggedIn")
            : LanguageControl.Get("ChineseCommunityExternalContentProvider", "NotLoggedIn");

        // 登录态恢复（EnsureStoredAuthLoaded）由全局帧泵每帧幂等驱动，此处纯读。
        public bool IsLoggedIn => AuthSessionManager.Instance.IsLoggedIn;

        // 列举能力存在，但载体是专用屏（社区接口为帖子/分页形状，非文件夹）：
        // ExternalContentScreen 换源选中本提供者时按 ScreenNameForListing 跳转，不经 List。
        public bool SupportsListing => true;

        public string ScreenNameForListing => "CommunityMyPublished";

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

        // 列举/下载由 CommunityMyPublishedScreen 承载（帖子/分页形状，无文件夹语义），provider 侧保留未实现；
        // Link（旧 Schub 的 create_shared_link 换链）新接口无对应端点——分享链接即文件 URL，由屏内直接复制。
        public void List(string path, CancellableProgress progress, Action<ExternalContentEntry> success, Action<Exception> failure)
            => throw new NotImplementedException();

        public void Download(string path, CancellableProgress progress, Action<Stream> success, Action<Exception> failure)
            => throw new NotImplementedException();

        public void Link(string path, CancellableProgress progress, Action<string> success, Action<Exception> failure)
            => throw new NotImplementedException();

        // 新接口发布链（同模组 CommunityUploadClient 工作流）：取又拍云直传凭证 → 直传文件服务 → POST /upload 登记。
        // path 仅用作文件名（ShowUploadUi 传裸文件名），类型按扩展名归一；用户取消以异常对象回调
        // （ShowUploadUi 的 failure 直接解引用 error.Message，不得传 null）。
        private static readonly CommunityUploadClient s_uploadClient = new(HttpClientProvider.Upload);

        public void Upload(string path, Stream stream, CancellableProgress progress, Action<string> success, Action<Exception> failure) {
            _ = UploadAsync(path, stream, progress, success, failure);
        }

        private async Task UploadAsync(string path, Stream stream, CancellableProgress progress, Action<string> success, Action<Exception> failure) {
            try {
                string fileName = Storage.GetFileName(path ?? string.Empty);
                if (string.IsNullOrWhiteSpace(fileName)) {
                    throw new InvalidOperationException(LanguageControl.Get("ChineseCommunityExternalContentProvider", "CannotFindLocalFile"));
                }
                CommunityFileType fileType = MapUploadFileType(ExternalContentManager.ExtensionToType(Storage.GetExtension(fileName).ToLower()));
                string token = AuthSessionManager.Instance.CurrentToken;
                string apiBaseUrl = AuthSessionManager.Instance.ApiBaseUrl;
                string saveKey = CommunityUploadFileNaming.MakeSaveKey(fileName, DateTime.UtcNow);
                // 凭证/登记是小请求：30s 有限超时防 API 静默挂起；上传本体由无限超时的
                // Upload 客户端池承载（HttpClientProvider.Upload 注释），取消统一走令牌。
                using CancellationTokenSource credentialsCts = CancellationTokenSource.CreateLinkedTokenSource(progress.CancellationToken);
                credentialsCts.CancelAfter(TimeSpan.FromSeconds(30));
                UpyunCredentials credentials = await s_uploadClient.GetUpyunCredentialsAsync(apiBaseUrl, token, saveKey, fileType, credentialsCts.Token);
                string uploadedUrl = await s_uploadClient.UploadToUpyunAsync(
                    credentials,
                    stream,
                    fileName,
                    delegate(float percent) {
                        progress.Total = 100f;
                        progress.Completed = Math.Clamp(percent, 0f, 100f);
                    },
                    progress.CancellationToken);
                using CancellationTokenSource registerCts = CancellationTokenSource.CreateLinkedTokenSource(progress.CancellationToken);
                registerCts.CancelAfter(TimeSpan.FromSeconds(30));
                UploadFileData fileData = await s_uploadClient.RegisterUploadedFileAsync(apiBaseUrl, token, uploadedUrl, fileType, registerCts.Token);
                success?.Invoke(fileData?.Url ?? uploadedUrl);
            }
            catch (OperationCanceledException) when (progress.CancellationToken.IsCancellationRequested) {
                failure?.Invoke(new InvalidOperationException(LanguageControl.Cancel));
            }
            catch (OperationCanceledException) {
                // 凭证/登记 30s 超时（"A task was canceled." 对用户无意义），映射网络错误文案。
                failure?.Invoke(new InvalidOperationException(LanguageControl.Get("ScbbsPlusDownloadFlowManager", "NetworkError")));
            }
            catch (Exception ex) {
                failure?.Invoke(ex);
            }
        }

        private static CommunityFileType MapUploadFileType(ExternalContentType type) => type switch {
            ExternalContentType.World => CommunityFileType.World,
            ExternalContentType.FurniturePack => CommunityFileType.Furniture,
            ExternalContentType.BlocksTexture => CommunityFileType.Texture,
            ExternalContentType.CharacterSkin => CommunityFileType.Skin,
            ExternalContentType.Mod => CommunityFileType.Mod,
            _ => throw new InvalidOperationException($"Unsupported upload type: {type}")
        };

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
