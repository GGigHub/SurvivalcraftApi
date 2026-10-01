using System.Globalization;
using System.Security.Cryptography;
using Engine;

namespace Game {
    /// <summary>
    /// 社区认证会话的进程级控制器：登录/登出任务轮询、本地凭据持久化、token 失效（401/403）监听。
    /// 三屏共用——社区屏持有入口按钮；本地屏/我的发布屏只在需要 token 的操作前弹登录框。
    /// 界面刷新通过 LoginStateChanged 由订阅方处理，控制器不持有任何屏幕控件；
    /// 唯一的直接 UI 输出是全局对话框与全局 Toast（DialogsManager / CommunityToastManager）。
    /// </summary>
    sealed class AuthSessionManager {
        public static readonly AuthSessionManager Instance = new();

        static AuthSessionManager() => CommunityUnauthorizedNotifier.UnauthorizedResponse += HandleUnauthorizedResponse;

        public enum AuthChangeKind {
            LoggedIn,
            LoggedOut,
            SessionExpired,
            LoginCanceled
        }

        // 认证客户端与会话存储本控制器独占（登录/登出/凭据持久化），不再经共享组合根转发。
        // static readonly：控制器是进程单例，存储须同样进程级，不随实例重建。
        static readonly CommunityAuthClient s_authClient = new(HttpClientProvider.Community);
        static readonly AuthenticationSessionStore s_authenticationSessionStore = new();
        Task<LoginResponse> m_loginTask;

        CancellationTokenSource m_loginCancellation;

        // 登录/登出目标服快照：请求在途时用户可能已切服，完成回调按快照读写，
        // 不得触碰当前服的会话与凭据文件。
        string m_loginApiBaseUrl;
        string m_logoutApiBaseUrl;
        Task<bool> m_logoutTask;
        CommunityLoginDialog m_loginDialog;

        bool m_storedAuthLoaded;

        // 内存会话表：归一化地址 → 该服登录数据。任意时刻只有当前地址是活跃上下文。
        readonly Dictionary<string, LoginData> m_sessions = new(StringComparer.OrdinalIgnoreCase);

        // 每 URL 每进程只读一次盘（含查空结果）。
        readonly HashSet<string> m_sessionLoadAttempted = new(StringComparer.OrdinalIgnoreCase);

        // 401/403 并发多请求只处理一次：首个触发置 1 并派发，派发执行后复位允许下次过期再触发。
        int m_unauthorizedHandled;

        // 弱引用事件源：本单例与屏幕生命周期不对等——换语言时引擎
        // 整表重建屏幕且旧实例不获 Leave，强事件会把弃屏钉到进程退出。弱源只持目标弱引用，
        // 屏幕侧退订由 ScreenBase.Dispose 确定性完成，死条目触发时自动剔除。
        readonly WeakEventSource<AuthChangeKind> m_loginStateChanged = new();

        public event Action<AuthChangeKind> LoginStateChanged {
            add => m_loginStateChanged.Subscribe(value);
            remove => m_loginStateChanged.Unsubscribe(value);
        }

        LoginData CurrentSession => m_sessions.TryGetValue(ApiBaseUrl, out LoginData session) ? session : null;

        public string CurrentToken => CurrentSession?.Token;

        public UserData CurrentUser => CurrentSession?.User;

        public bool IsLoggedIn => CurrentSession != null;

        public bool IsBusy => m_loginTask != null || m_logoutTask != null;

        /// <summary>
        /// 当前社区服务器地址（归一化）：直接派生自 CommunityServerManager.CurrentChineseInfo，
        /// 本类不自持地址存储；服务器切换由 CommunityServerManager.ChangeChineseInfo 驱动。
        /// </summary>
        public string ApiBaseUrl => CommunityServerManager.CurrentApiBaseUrl;

        // 会话激活去重指针：记录最近一次加载过会话的地址，同地址重复通知为 no-op。
        string m_loadedSessionUrl;

        /// <summary>
        /// 服务器切换通知：激活目标服会话。per-URL 会话下切服不登出——
        /// 各服登录态独立保存、独立恢复；同地址重复通知为 no-op。
        /// </summary>
        public void OnServerSwitched() {
            string current = ApiBaseUrl;
            if (string.Equals(m_loadedSessionUrl, current, StringComparison.OrdinalIgnoreCase)) {
                return;
            }
            m_loadedSessionUrl = current;
            m_loginStateChanged.Raise(IsLoggedIn ? AuthChangeKind.LoggedIn : AuthChangeKind.LoggedOut);
        }

        /// <summary>认证入口：已登录弹登出确认，否则弹登录对话框。</summary>
        public void OpenAuthEntry() {
            if (IsLoggedIn) {
                DialogsManager.ShowDialog(
                    null,
                    new MessageDialog(
                        LanguageControl.Get("AuthSessionManager", "LogOut"),
                        LanguageControl.Get("AuthSessionManager", "LogOutOfTheCurrentCommunityAccount"),
                        LanguageControl.Yes,
                        LanguageControl.No,
                        button => {
                            if (button == MessageDialogButton.Button1) {
                                BeginLogout();
                            }
                        }
                    )
                );
                return;
            }
            if (m_loginTask == null) {
                m_loginDialog = new CommunityLoginDialog(
                    BeginLogin,
                    () => {
                        // 关闭对话框即放弃本次登录：取消在途请求，迟到结果不写入登录态。
                        m_loginCancellation?.Cancel();
                        m_loginDialog = null;
                        m_loginStateChanged.Raise(AuthChangeKind.LoginCanceled);
                    }
                );
                DialogsManager.ShowDialog(null, m_loginDialog);
            }
        }

        void BeginLogin(string account, string password) {
            if (m_loginTask != null) {
                return;
            }
            account = account?.Trim();
            if (string.IsNullOrEmpty(account)
                || string.IsNullOrEmpty(password)) {
                m_loginDialog?.SetError(LanguageControl.Get("AuthSessionManager", "PleaseEnterAccountAndPassword"));
                return;
            }
            m_loginCancellation = new CancellationTokenSource();
            string loginApiBaseUrl = ApiBaseUrl;
            m_loginApiBaseUrl = loginApiBaseUrl;
            try {
                m_loginTask = s_authClient.LoginAsync(loginApiBaseUrl, account, password, m_loginCancellation.Token);
            }
            catch (Exception ex) {
                m_loginApiBaseUrl = null;
                // 服务创建任务时若同步失败，也要释放 CTS 并恢复登录对话框。
                m_loginCancellation.Dispose();
                m_loginCancellation = null;
                Log.Information(
                    string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("AuthSessionManager", "CreateLoginRequestFailed"), ex.Message)
                );
                m_loginDialog?.SetBusy(false);
                m_loginDialog?.SetError(ex.Message);
                return;
            }
            m_loginDialog?.SetBusy(true);
        }

        public void BeginLogout() {
            if (m_logoutTask != null) {
                return;
            }
            LoginData session = CurrentSession;
            if (session == null) {
                return;
            }
            string apiBaseUrl = ApiBaseUrl;
            m_logoutApiBaseUrl = apiBaseUrl;
            m_logoutTask = s_authClient.LogoutAsync(apiBaseUrl, session.Token);

            // 内存先清该服条目；凭据文件等服务端登出成功后删除（见 Update 轮询）：
            // 服务端失败时保留文件供重试，避免删了本地却残留服务端会话。
            m_sessions.Remove(apiBaseUrl);
            m_loginStateChanged.Raise(AuthChangeKind.LoggedOut);
        }

        /// <summary>
        /// 幂等恢复本地凭据（原屏幕 Enter 延迟工作里的 LoadStoredAuth）。恢复可能同步触发
        /// LoginStateChanged(LoggedIn)。不经屏幕 Update 而直接进入的屏（如我的发布屏）必须在
        /// Enter 里先调本方法，否则 IsLoggedIn 恒 false，已登录用户也会被弹登录框。
        /// </summary>
        public void EnsureStoredAuthLoaded() {
            if (m_storedAuthLoaded) {
                return;
            }
            m_storedAuthLoaded = true;
            LoadSessionFromDisk(ApiBaseUrl);
        }

        void LoadSessionFromDisk(string apiBaseUrl) {
            if (!m_sessionLoadAttempted.Add(apiBaseUrl)) {
                return;
            }
            try {
                LoginData data = s_authenticationSessionStore.Load(apiBaseUrl);
                if (data != null
                    && !string.IsNullOrEmpty(data.Token)
                    && data.User != null) {
                    m_sessions[apiBaseUrl] = data;
                    if (string.Equals(apiBaseUrl, ApiBaseUrl, StringComparison.OrdinalIgnoreCase)) {
                        m_loginStateChanged.Raise(AuthChangeKind.LoggedIn);
                    }
                }
            }
            catch (PlatformNotSupportedException ex) {
                Log.Warning(
                    string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("AuthSessionManager", "ClearedUnreadableCredentials"), ex.Message)
                );
                s_authenticationSessionStore.TryDelete(apiBaseUrl);
            }
            catch (CryptographicException) {
                Log.Information(LanguageControl.Get("AuthSessionManager", "CredentialDecryptionFailedClearedOld"));
                s_authenticationSessionStore.TryDelete(apiBaseUrl);
            }
            catch (Exception ex) {
                Log.Error(
                    string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("AuthSessionManager", "LoadCredentialsFailed"), ex.Message)
                );
            }
        }

        public void Update() {
            // 异常兜底内置（全局帧泵调用）：引擎 Frame?.Invoke() 无 try/catch，冒泡即杀进程。
            try {
                EnsureStoredAuthLoaded();

                // 轮询登录结果
                if (m_loginTask != null
                    && m_loginTask.IsCompleted) {
                    LoginResponse result;
                    try {
                        result = m_loginTask.Result;
                    }
                    catch (Exception ex) {
                        // 用户取消登录等异常不冒泡到游戏帧循环，按失败路径展示。
                        Log.Information(
                            string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("AuthSessionManager", "LoginRequestFaulted"), ex.Message)
                        );
                        result = null;
                    }
                    m_loginTask = null;
                    bool loginAbandoned = m_loginCancellation != null && m_loginCancellation.IsCancellationRequested;
                    m_loginCancellation?.Dispose();
                    m_loginCancellation = null;
                    if (loginAbandoned) {
                        m_loginApiBaseUrl = null;
                        // 登录对话框已关闭：迟到结果不写入登录态，也不弹错误窗。
                        return;
                    }
                    if (result != null
                        && result.Code == 200
                        && result.Data?.User != null) {
                        // 登录成功：按发起时快照写入目标服（在途期间可能已切服）。
                        string loginApiBaseUrl = m_loginApiBaseUrl ?? ApiBaseUrl;
                        m_sessions[loginApiBaseUrl] = new LoginData { Token = result.Data.Token, User = result.Data.User };
                        if (m_loginDialog != null) {
                            DialogsManager.HideDialog(m_loginDialog);
                            m_loginDialog = null;
                        }

                        // 持久化登录数据
                        SaveStoredAuth(loginApiBaseUrl);
                        if (string.Equals(loginApiBaseUrl, ApiBaseUrl, StringComparison.OrdinalIgnoreCase)) {
                            m_loginStateChanged.Raise(AuthChangeKind.LoggedIn);
                        }
                    }
                    else {
                        string errorMessage = result?.Msg ?? LanguageControl.Get("AuthSessionManager", "LoginFailedDefaultMessage");
                        Log.Information(
                            string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("AuthSessionManager", "LoginFailed"), errorMessage)
                        );
                        CommunityLoginDialog loginDialog = m_loginDialog;
                        if (loginDialog == null) {
                            DialogsManager.ShowDialog(null, new MessageDialog(LanguageControl.Error, errorMessage, LanguageControl.Ok, null, null));
                        }
                        else if (IsDialogAttachedToRoot(loginDialog)) {
                            loginDialog.SetBusy(false);
                            loginDialog.SetError(errorMessage);
                        }
                        else {
                            // 引擎切屏只把旧屏从 RootWidget 摘除，对话框仍留在 Dialogs（挂在死屏上），
                            // 错误写上去用户看不见：改走 MessageDialog 兜底。HideDialog 同步把死框移出
                            // Dialogs，关闭动画再把死框连同旧 CoverWidget 从死屏摘除。
                            // 兜底 MessageDialog 是另一实例，不同字典键，无同帧 Hide+Show 覆盖 AnimationData 之虞。
                            m_loginDialog = null;
                            DialogsManager.HideDialog(loginDialog);
                            DialogsManager.ShowDialog(null, new MessageDialog(LanguageControl.Error, errorMessage, LanguageControl.Ok, null, null));
                        }
                    }
                    // 登录结果处理完毕（成功/失败均至此）：清除目标服快照。
                    m_loginApiBaseUrl = null;
                }
                if (m_logoutTask != null
                    && m_logoutTask.IsCompleted) {
                    try {
                        if (m_logoutTask.Result) {
                            // 服务端登出成功：删除该服本地凭据（BeginLogout 阶段仅清了内存态）。
                            DeleteStoredAuth(m_logoutApiBaseUrl ?? ApiBaseUrl);
                        }
                        else {
                            // 服务端登出失败：本地凭据已保留（可重试登录/登出），Toast 说明。
                            CommunityToastManager.Show(LanguageControl.Get("AuthSessionManager", "LogoutFailedToast"), autoHideSeconds: 4.0);
                        }
                    }
                    catch (Exception ex) {
                        Log.Warning(
                            string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("AuthClient", "LogoutRequestFailed"), ex.Message)
                        );
                    }
                    m_logoutTask = null;
                    m_logoutApiBaseUrl = null;
                }
            }
            catch (Exception ex) {
                Log.Error(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("AuthSessionManager", "AuthSessionPollFaulted"), ex));
            }
        }

        void SaveStoredAuth(string apiBaseUrl) {
            LoginData session = m_sessions.TryGetValue(CommunityServerManager.NormalizeApiBaseUrl(apiBaseUrl), out LoginData value) ? value : null;
            if (session == null) {
                return;
            }
            try {
                Exception error = s_authenticationSessionStore.TrySave(apiBaseUrl, session.Token, session.User);
                if (error != null) {
                    throw error;
                }
            }
            catch (PlatformNotSupportedException ex) {
                try {
                    s_authenticationSessionStore.TryDelete(apiBaseUrl);
                }
                catch {
                    // ignored
                }
                Log.Warning($"Saving community login credentials is not supported on this platform: {ex.Message}");
            }
            catch (Exception ex) {
                Log.Error(
                    string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("AuthSessionManager", "SaveCredentialsFailed"), ex.Message)
                );
            }
        }

        static void DeleteStoredAuth(string apiBaseUrl) {
            try {
                Exception error = s_authenticationSessionStore.TryDelete(apiBaseUrl);
                if (error != null) {
                    throw error;
                }
            }
            catch (Exception ex) {
                Log.Error(
                    string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("AuthSessionManager", "DeleteCredentialFileFailed"), ex.Message)
                );
            }
        }

        // 存活判据不能只查 Dialogs：切屏后对话框实例仍留在 Dialogs（挂在死屏上，ShowDialog 对在册实例是 no-op），
        // 须「在 Dialogs 中且父链可达 RootWidget」（与下载覆盖确认框同一机制，见 QueueDialog 处的同型修复）。
        static bool IsDialogAttachedToRoot(Dialog dialog) => dialog != null
            && DialogsManager.Dialogs.Contains(dialog)
            && IsWidgetInRootHierarchy(dialog);

        static bool IsWidgetInRootHierarchy(Widget widget) {
            while (widget != null) {
                if (widget == ScreensManager.RootWidget) {
                    return true;
                }
                widget = widget.ParentWidget;
            }
            return false;
        }

        static void HandleUnauthorizedResponse(string token) {
            Instance.HandleUnauthorizedResponseCore(token);
        }

        void HandleUnauthorizedResponseCore(string token) {
            // 后台请求线程回调。token 为空无从定位归属，忽略。
            if (token == null) {
                return;
            }
            // 并发多个 401 只派发一次：Exchange 置 1 成功者派发，其余忽略；派发执行后复位。
            if (Interlocked.Exchange(ref m_unauthorizedHandled, 1) != 0) {
                return;
            }
            Dispatcher.Dispatch(
                () => {
                    m_unauthorizedHandled = 0;
                    // token 全表定位归属服：旧 token 的在途请求（发起时按值复制了 token）在重新登录/
                    // 切服后才返回 401，只清其所属会话，不误杀其他服与当前服。
                    string ownerUrl = null;
                    foreach (KeyValuePair<string, LoginData> kvp in m_sessions) {
                        if (string.Equals(kvp.Value.Token, token, StringComparison.Ordinal)) {
                            ownerUrl = kvp.Key;
                            break;
                        }
                    }
                    if (ownerUrl == null) {
                        return;
                    }
                    m_sessions.Remove(ownerUrl);
                    DeleteStoredAuth(ownerUrl);
                    if (string.Equals(ownerUrl, ApiBaseUrl, StringComparison.OrdinalIgnoreCase)) {
                        Log.Information(LanguageControl.Get("AuthSessionManager", "TokenInvalidatedClearingLocalState"));
                        m_loginStateChanged.Raise(AuthChangeKind.SessionExpired);
                        DialogsManager.ShowDialog(
                            null,
                            new MessageDialog(
                                LanguageControl.Get("AuthSessionManager", "TokenExpiredTitle"),
                                LanguageControl.Get("AuthSessionManager", "TokenExpired"),
                                LanguageControl.Ok,
                                null,
                                null
                            )
                        );
                    }
                },
                true
            );
        }
    }
}