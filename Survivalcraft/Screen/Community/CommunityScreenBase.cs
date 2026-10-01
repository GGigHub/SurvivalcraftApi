namespace Game {
    /// <summary>
    /// 四屏（社区/本地/我的发布/More）公共样板：
    /// ctor 订阅静态事件，Dispose（换语言整表重建的唯一生命周期终点）确定性退订；
    /// IRemoteImportHost 默认实现；下载控制器单槽委托（StatusReporter/远程导入主机）
    /// 的 Enter 安装与 Leave 释放。ScreensManager 的 Enter/Leave 顺序不保证相邻帧配对，
    /// 单槽委托释放一律“只在自己仍持有时清空”。
    /// </summary>
    abstract class CommunityScreenBase : Screen, IRemoteImportHost {
        protected CommunityScreenBase() =>
            // 登录态变化：认证会话抽到 AuthSessionManager，屏幕只订阅刷新界面。
            AuthSessionManager.Instance.LoginStateChanged += HandleLoginStateChanged;

        bool m_disposed;

        public override void Dispose() {
            if (m_disposed) {
                return;
            }
            m_disposed = true;
            // 换语言整表重建（LanguageControl.ChangeLanguage）不调 Leave，是本类实例
            // 唯一的生命周期终点：在那里被 Dispose 时确定性退订 ctor 订阅的静态事件。
            AuthSessionManager.Instance.LoginStateChanged -= HandleLoginStateChanged;
            base.Dispose();
        }

        void HandleLoginStateChanged(AuthSessionManager.AuthChangeKind kind) {
            OnLoginStateChanged(kind);
        }

        /// <summary>登录态变化处理；订阅退订由 Dispose 确定性完成。</summary>
        protected abstract void OnLoginStateChanged(AuthSessionManager.AuthChangeKind kind);

        bool IRemoteImportHost.HasExactLocalContent(ExternalContentType type, string fileName) =>
            LocalModStateStore.FindExactLocalDownloadFilePath(type, fileName) != null;

        bool IRemoteImportHost.IsModpackDataFilePresent(ModpackItem item) =>
            // 社区/More/我的发布屏不处理整合包数据文件（本地屏职责）。
            IsModpackDataFilePresent(item);

        void IRemoteImportHost.ImportModpackDataFileSafe(byte[] data, ModpackItem item) {
            ImportModpackDataFileSafe(data, item);
        }

        protected virtual bool IsModpackDataFilePresent(ModpackItem item) => false;

        protected virtual void ImportModpackDataFileSafe(byte[] data, ModpackItem item) { }

        /// <summary>
        /// 进入屏幕时装载下载控制器单槽委托。EnsureLoaded 等屏内顺序由子类自行安排；
        /// 远程导入主机的注册见 AcquireRemoteImportHost / SetDownloadFlowRemoteImportHost。
        /// </summary>
        protected void InstallDownloadFlowCallbacks(Action<string> statusReporter,
            Func<bool> manualSelectResolver,
            Func<CommunityDownloadSourceManager.Info> effectiveSourceResolver) {
            DownloadFlowManager downloadManager = DownloadFlowManager.Instance;
            downloadManager.StatusReporter = statusReporter;
            downloadManager.ManualSelectResolver = manualSelectResolver;
            downloadManager.EffectiveSourceResolver = effectiveSourceResolver;
        }

        /// <summary>社区/More 屏进入时同时注册顶层导入对话框主机与下载流程导入主机。</summary>
        protected void AcquireRemoteImportHost() {
            RemoteImportManager.RegisterHost(this);
            DownloadFlowManager.Instance.SetRemoteImportHost(this);
        }

        /// <summary>本地屏进入时只接管下载流程导入主机（顶层导入对话框主机仍归社区/More 屏）。</summary>
        protected void SetDownloadFlowRemoteImportHost(bool active) {
            if (active) {
                DownloadFlowManager.Instance.SetRemoteImportHost(this);
            }
            else {
                DownloadFlowManager.Instance.ReleaseRemoteImportHost(this);
            }
        }

        /// <summary>
        /// 离开屏幕时保存下载队列；
        /// releaseRemoteImportHost 的屏幕（社区/More/本地）还注销导入主机。
        /// </summary>
        protected void ReleaseDownloadFlowCallbacks(bool releaseRemoteImportHost) {
            DownloadFlowManager downloadManager = DownloadFlowManager.Instance;
            downloadManager.SaveQueue();
            downloadManager.ReleaseRemoteImportHost(this);
            if (releaseRemoteImportHost) {
                RemoteImportManager.UnregisterHost(this);
            }
        }
    }
}