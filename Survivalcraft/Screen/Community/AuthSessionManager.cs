using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Engine;

namespace Game
{
    /// <summary>
    /// 社区认证会话的进程级控制器：登录/登出任务轮询、本地凭据持久化、token 失效（401/403）监听。
    /// 三屏共用——社区屏持有入口按钮；本地屏/我的发布屏只在需要 token 的操作前弹登录框。
    /// 界面刷新通过 LoginStateChanged 由订阅方处理，控制器不持有任何屏幕控件；
    /// 唯一的直接 UI 输出是全局对话框与全局 Toast（DialogsManager / GlobalToastManager）。
    /// </summary>
    internal sealed class AuthSessionManager
    {
        public static readonly AuthSessionManager Instance = new();

        static AuthSessionManager()
        {
            UnauthorizedNotifier.UnauthorizedResponse += HandleUnauthorizedResponse;
        }

        public enum AuthChangeKind
        {
            LoggedIn,
            LoggedOut,
            SessionExpired,
            LoginCanceled
        }

        // 认证客户端与会话存储本控制器独占（登录/登出/凭据持久化），不再经共享组合根转发。
        // static readonly：控制器是进程单例，存储须同样进程级，不随实例重建。
        private static readonly AuthClient s_authClient = new(HttpClientProvider.Community);
        private static readonly AuthenticationSessionStore s_authenticationSessionStore = new(StoragePaths.AuthFile);
        private Task<LoginResponse> m_loginTask;
        private CancellationTokenSource m_loginCancellation;
        private Task<bool> m_logoutTask;
        private CommunityLoginDialog m_loginDialog;
        private bool m_storedAuthLoaded;
        // 401/403 并发多请求只处理一次：首个触发置 1 并派发，派发执行后复位允许下次过期再触发。
        private int m_unauthorizedHandled;

        // 弱引用事件源：本单例与屏幕生命周期不对等——换语言时引擎
        // 整表重建屏幕且旧实例不获 Leave，强事件会把弃屏钉到进程退出。弱源只持目标弱引用，
        // ScreenBase 的配对退订保留（显式 -== 与存活自救不变），死条目触发时自动剔除。
        private readonly WeakEventSource<AuthChangeKind> m_loginStateChanged = new();

        public event Action<AuthChangeKind> LoginStateChanged
        {
            add => m_loginStateChanged.Subscribe(value);
            remove => m_loginStateChanged.Unsubscribe(value);
        }

        public string CurrentToken { get; private set; }

        public UserData CurrentUser { get; private set; }

        public bool IsLoggedIn { get; private set; }

        public bool IsBusy => m_loginTask != null || m_logoutTask != null;

        /// <summary>目标社区服务器地址；由设置加载/切换流程写入，登录登出请求读取。</summary>
        public string ApiBaseUrl
        {
            get => string.IsNullOrWhiteSpace(m_apiBaseUrl) ? CommunitySettingsManager.DefaultApiBaseUrl : m_apiBaseUrl;
            set => m_apiBaseUrl = value;
        }

        private string m_apiBaseUrl = CommunitySettingsManager.DefaultApiBaseUrl;

        /// <summary>认证入口：已登录弹登出确认，否则弹登录对话框。</summary>
        public void OpenAuthEntry()
        {
            if (IsLoggedIn)
            {
                DialogsManager.ShowDialog(
                    null,
                    new MessageDialog(
                        LanguageControl.Get("ScbbsPlusCommunityScreen", "Auth", "Auth", "LogOut"),
                        LanguageControl.Get("ScbbsPlusCommunityScreen", "Auth", "Auth", "LogOutOfTheCurrentCommunityAccount"),
                        LanguageControl.Yes,
                        LanguageControl.No,
                        button =>
                        {
                            if (button == MessageDialogButton.Button1)
                            {
                                BeginLogout();
                            }
                        }
                    )
                );
                return;
            }

            if (m_loginTask == null)
            {
                m_loginDialog = new CommunityLoginDialog(BeginLogin, () =>
                {
                    // 关闭对话框即放弃本次登录：取消在途请求，迟到结果不写入登录态。
                    m_loginCancellation?.Cancel();
                    m_loginDialog = null;
                    m_loginStateChanged.Raise(AuthChangeKind.LoginCanceled);
                });
                DialogsManager.ShowDialog(null, m_loginDialog);
            }
        }

        private void BeginLogin(string account, string password)
        {
            if (m_loginTask != null)
            {
                return;
            }

            account = account?.Trim();
            if (string.IsNullOrEmpty(account) || string.IsNullOrEmpty(password))
            {
                m_loginDialog?.SetError(LanguageControl.Get("ScbbsPlusCommunityScreen", "Auth", "Auth", "PleaseEnterAccountAndPassword"));
                return;
            }

            m_loginCancellation = new CancellationTokenSource();
            try
            {
                m_loginTask = s_authClient.LoginAsync(
                    ApiBaseUrl,
                    account,
                    password,
                    m_loginCancellation.Token);
            }
            catch (Exception ex)
            {
                // 服务创建任务时若同步失败，也要释放 CTS 并恢复登录对话框。
                m_loginCancellation.Dispose();
                m_loginCancellation = null;
                Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusAuthSessionManager", "CreateLoginRequestFailed"), ex.Message));
                m_loginDialog?.SetBusy(false);
                m_loginDialog?.SetError(ex.Message);
                return;
            }
            m_loginDialog?.SetBusy(true);
        }

        public void BeginLogout()
        {
            if (m_logoutTask != null)
            {
                return;
            }

            string token = CurrentToken;
            string apiBaseUrl = ApiBaseUrl;
            m_logoutTask = s_authClient.LogoutAsync(apiBaseUrl, token);

            // 本地凭据等服务端登出成功后删除（见 Update 的 m_logoutTask 轮询）：
            // 服务端失败时保留本地凭据供重试，避免删了本地却残留服务端会话。
            ResetSession(clearStoredAuth: false);
            m_loginStateChanged.Raise(AuthChangeKind.LoggedOut);
        }

        /// <summary>
        /// 幂等恢复本地凭据（原屏幕 Enter 延迟工作里的 LoadStoredAuth）。恢复可能同步触发
        /// LoginStateChanged(LoggedIn)。不经屏幕 Update 而直接进入的屏（如我的发布屏）必须在
        /// Enter 里先调本方法，否则 IsLoggedIn 恒 false，已登录用户也会被弹登录框。
        /// </summary>
        public void EnsureStoredAuthLoaded()
        {
            if (m_storedAuthLoaded)
            {
                return;
            }
            m_storedAuthLoaded = true;
            LoadStoredAuth();
        }

        public void Update()
        {
            EnsureStoredAuthLoaded();

            // 轮询登录结果
            if (m_loginTask != null && m_loginTask.IsCompleted)
            {
                LoginResponse result;
                try
                {
                    result = m_loginTask.Result;
                }
                catch (Exception ex)
                {
                    // 用户取消登录等异常不冒泡到游戏帧循环，按失败路径展示。
                    Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusAuthSessionManager", "LoginRequestFaulted"), ex.Message));
                    result = null;
                }
                m_loginTask = null;
                bool loginAbandoned = m_loginCancellation != null && m_loginCancellation.IsCancellationRequested;
                m_loginCancellation?.Dispose();
                m_loginCancellation = null;
                if (loginAbandoned)
                {
                    // 登录对话框已关闭：迟到结果不写入登录态，也不弹错误窗。
                    return;
                }

                if (result != null && result.Code == 200 && result.Data?.User != null)
                {
                    // 登录成功
                    CurrentToken = result.Data.Token;
                    CurrentUser = result.Data.User;
                    IsLoggedIn = true;
                    if (m_loginDialog != null)
                    {
                        DialogsManager.HideDialog(m_loginDialog);
                        m_loginDialog = null;
                    }

                    Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusAuthSessionManager", "LoginSucceeded"), CurrentUser.Nickname, CurrentUser.Level));

                    // 持久化登录数据
                    SaveStoredAuth();

                    m_loginStateChanged.Raise(AuthChangeKind.LoggedIn);
                }
                else
                {
                    string errorMessage = result?.Msg ?? LanguageControl.Get("ScbbsPlusCommunityScreen", "Main", "LoginFailed");
                    Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusAuthSessionManager", "LoginFailed"), errorMessage));
                    CommunityLoginDialog loginDialog = m_loginDialog;
                    if (loginDialog == null)
                    {
                        DialogsManager.ShowDialog(null, new MessageDialog(LanguageControl.Error, errorMessage, LanguageControl.Ok, null, null));
                    }
                    else if (IsDialogAttachedToRoot(loginDialog))
                    {
                        loginDialog.SetBusy(false);
                        loginDialog.SetError(errorMessage);
                    }
                    else
                    {
                        // 引擎切屏只把旧屏从 RootWidget 摘除，对话框仍留在 Dialogs（挂在死屏上），
                        // 错误写上去用户看不见：改走 MessageDialog 兜底。HideDialog 同步把死框移出
                        // Dialogs，关闭动画再把死框连同旧 CoverWidget 从死屏摘除。
                        // 兜底 MessageDialog 是另一实例，不同字典键，无同帧 Hide+Show 覆盖 AnimationData 之虞。
                        m_loginDialog = null;
                        DialogsManager.HideDialog(loginDialog);
                        DialogsManager.ShowDialog(null, new MessageDialog(LanguageControl.Error, errorMessage, LanguageControl.Ok, null, null));
                    }
                }
            }

            if (m_logoutTask != null && m_logoutTask.IsCompleted)
            {
                try
                {
                    if (m_logoutTask.Result)
                    {
                        // 服务端登出成功：删除本地凭据（BeginLogout 阶段仅清了内存态）。
                        DeleteStoredAuth();
                    }
                    else
                    {
                        // 服务端登出失败：本地凭据已保留（可重试登录/登出），Toast 说明。
                        GlobalToastManager.Show(
                            LanguageControl.Get("ScbbsPlusCommunityScreen", "Auth", "Auth", "LogoutFailedToast"),
                            autoHideSeconds: 4.0);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusAuthClient", "LogoutRequestFailed"), ex.Message));
                }
                m_logoutTask = null;
            }
        }

        public void LoadStoredAuth()
        {
            try
            {
                AuthenticationSessionLoadResult loadResult = s_authenticationSessionStore.Load();
                if (loadResult.Error != null)
                {
                    throw loadResult.Error;
                }
                LoginData data = loadResult.Data;
                bool legacyFormat = loadResult.IsLegacyFormat;

                if (data != null && !string.IsNullOrEmpty(data.Token) && data.User != null)
                {
                    CurrentToken = data.Token;
                    CurrentUser = data.User;
                    IsLoggedIn = true;

                    Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusAuthSessionManager", "LoginStateRestoredFromLocal"), CurrentUser.Nickname));

                    if (legacyFormat)
                    {
                        if (OperatingSystem.IsWindows())
                        {
                            if (SaveStoredAuth())
                            {
                                Log.Information(LanguageControl.Get("ScbbsPlusAuthSessionManager", "LegacyCredentialsMigratedToDpapi"));
                            }
                        }
                        else
                        {
                            // 非 Windows 平台把旧 AES-CBC 凭据迁移为 AES-GCM 格式。
                            SaveStoredAuth();
                        }
                    }

                    m_loginStateChanged.Raise(AuthChangeKind.LoggedIn);
                }
            }
            catch (PlatformNotSupportedException ex)
            {
                Log.Warning(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusAuthSessionManager", "ClearedUnreadableCredentials"), ex.Message));
                s_authenticationSessionStore.TryDelete();
            }
            catch (CryptographicException)
            {
                // DPAPI 用户/机器不匹配、旧文件损坏或密文被篡改时，清除并要求重新登录。
                Log.Information(LanguageControl.Get("ScbbsPlusAuthSessionManager", "CredentialDecryptionFailedClearedOld"));
                s_authenticationSessionStore.TryDelete();
            }
            catch (Exception ex)
            {
                Log.Error(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusAuthSessionManager", "LoadCredentialsFailed"), ex.Message));
            }
        }

        public bool SaveStoredAuth()
        {
            try
            {
                Exception error = s_authenticationSessionStore.TrySave(CurrentToken, CurrentUser);
                if (error != null)
                {
                    throw error;
                }
                Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusAuthSessionManager", "LoginCredentialsSavedEncrypted"), StoragePaths.AuthFile));
                return true;
            }
            catch (PlatformNotSupportedException ex)
            {
                try
                {
                    s_authenticationSessionStore.TryDelete();
                }
                catch
                {
                }
                Log.Warning($"[ScbbsPlus] {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                Log.Error(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusAuthSessionManager", "SaveCredentialsFailed"), ex.Message));
                return false;
            }
        }

        public void DeleteStoredAuth()
        {
            try
            {
                Exception error = s_authenticationSessionStore.TryDelete();
                if (error != null)
                {
                    throw error;
                }
                Log.Information(LanguageControl.Get("ScbbsPlusAuthSessionManager", "LocalCredentialFileDeleted"));
            }
            catch (Exception ex)
            {
                Log.Error(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusAuthSessionManager", "DeleteCredentialFileFailed"), ex.Message));
            }
        }

        // 存活判据不能只查 Dialogs：切屏后对话框实例仍留在 Dialogs（挂在死屏上，ShowDialog 对在册实例是 no-op），
        // 须「在 Dialogs 中且父链可达 RootWidget」（与下载覆盖确认框同一机制，见 QueueDialog 处的同型修复）。
        private static bool IsDialogAttachedToRoot(Dialog dialog)
        {
            return dialog != null
                && DialogsManager.Dialogs.Contains(dialog)
                && IsWidgetInRootHierarchy(dialog);
        }

        private static bool IsWidgetInRootHierarchy(Widget widget)
        {
            while (widget != null)
            {
                if (widget == ScreensManager.RootWidget)
                {
                    return true;
                }
                widget = widget.ParentWidget;
            }
            return false;
        }

        private void ResetSession(bool clearStoredAuth)
        {
            IsLoggedIn = false;
            CurrentToken = null;
            CurrentUser = null;

            if (clearStoredAuth)
            {
                DeleteStoredAuth();
            }
        }

        private static void HandleUnauthorizedResponse(string token)
        {
            Instance.HandleUnauthorizedResponseCore(token);
        }

        private void HandleUnauthorizedResponseCore(string token)
        {
            // 后台请求线程回调。先做廉价状态判定：已不在 LoggedIn 不打扰用户（如登出流程中）。
            if (!IsLoggedIn)
            {
                return;
            }
            // 并发多个 401 只派发一次：Exchange 置 1 成功者派发，其余忽略；派发执行后复位。
            if (Interlocked.Exchange(ref m_unauthorizedHandled, 1) != 0)
            {
                return;
            }
            Dispatcher.Dispatch(() =>
            {
                m_unauthorizedHandled = 0;
                // token 身份比对：旧 token 的在途请求（发起时按值复制了 token）在重新登录后
                // 才返回 401，不得误杀新会话。
                if (!IsLoggedIn
                    || token == null
                    || !string.Equals(token, CurrentToken, StringComparison.Ordinal))
                {
                    return;
                }
                Log.Information(LanguageControl.Get("ScbbsPlusAuthSessionManager", "TokenInvalidatedClearingLocalState"));
                ResetSession(clearStoredAuth: true);
                m_loginStateChanged.Raise(AuthChangeKind.SessionExpired);
                DialogsManager.ShowDialog(null, new MessageDialog(
                    LanguageControl.Get("ScbbsPlusCommunityScreen", "Core", "Ui", "TokenExpiredTitle"),
                    LanguageControl.Get("ScbbsPlusCommunityScreen", "Core", "Ui", "TokenExpired"),
                    LanguageControl.Ok,
                    null,
                    null));
            }, waitUntilCompleted: true);
        }
    }
}
