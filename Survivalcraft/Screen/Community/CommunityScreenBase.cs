using System;

namespace Game
{
    /// <summary>
    /// 四屏（社区/本地/我的发布/More）公共样板：
    /// 登录态事件订阅与换语言僵尸屏自检退订、IRemoteImportHost 默认实现、
    /// 下载控制器单槽委托（StatusReporter/RestartReturnRememberer/远程导入主机）的
    /// Enter 安装与 Leave 释放。ScreensManager 的 Enter/Leave 顺序不保证相邻帧配对，
    /// 单槽委托释放一律“只在自己仍持有时清空”。
    /// </summary>
    internal abstract class CommunityScreenBase : Screen, IRemoteImportHost
    {
        protected CommunityScreenBase()
        {
            // 登录态变化：认证会话抽到 AuthSessionManager，屏幕只订阅刷新界面。
            AuthSessionManager.Instance.LoginStateChanged += HandleLoginStateChanged;
        }

        private void HandleLoginStateChanged(AuthSessionManager.AuthChangeKind kind)
        {
            if (!CommunityScreenRegistry.IsOwningScreenLive(this))
            {
                // 换语言整表重建后本屏是弃实例：退订自救，避免静态事件把僵尸屏钉在内存。
                AuthSessionManager.Instance.LoginStateChanged -= HandleLoginStateChanged;
                return;
            }
            OnLoginStateChanged(kind);
        }

        /// <summary>屏幕存活时的登录态变化处理；僵尸屏自检退订已由基类完成。</summary>
        abstract protected void OnLoginStateChanged(AuthSessionManager.AuthChangeKind kind);

        /// <summary>记录重启返回页；IRemoteImportHost 与下载控制器共用同一实现。</summary>
        abstract protected void RememberRestartReturnToCurrentPage();

        bool IRemoteImportHost.HasExactLocalContent(ExternalContentType type, string fileName)
        {
            return LocalModStateStore.FindExactLocalDownloadFilePath(type, fileName) != null;
        }

        bool IRemoteImportHost.IsModpackDataFilePresent(ModpackItem item)
        {
            // 社区/More/我的发布屏不处理整合包数据文件（本地屏职责）。
            return IsModpackDataFilePresent(item);
        }

        void IRemoteImportHost.ImportModpackDataFileSafe(byte[] data, ModpackItem item)
        {
            ImportModpackDataFileSafe(data, item);
        }

        void IRemoteImportHost.RememberRestartReturnToCurrentPage()
        {
            RememberRestartReturnToCurrentPage();
        }

        virtual protected bool IsModpackDataFilePresent(ModpackItem item)
        {
            return false;
        }

        virtual protected void ImportModpackDataFileSafe(byte[] data, ModpackItem item)
        {
        }

        /// <summary>
        /// 进入屏幕时装载下载控制器单槽委托。EnsureLoaded 等屏内顺序由子类自行安排；
        /// 远程导入主机的注册见 AcquireRemoteImportHost / SetDownloadFlowRemoteImportHost。
        /// </summary>
        protected void InstallDownloadFlowCallbacks(
            Action<string> statusReporter,
            Func<DownloadSourceSelectionMode> sourceModeResolver,
            Func<DownloadSourceOption> effectiveSourceResolver)
        {
            DownloadFlowManager downloadManager = DownloadFlowManager.Instance;
            downloadManager.StatusReporter = statusReporter;
            downloadManager.RestartReturnRememberer = RememberRestartReturnToCurrentPage;
            downloadManager.SourceModeResolver = sourceModeResolver;
            downloadManager.EffectiveSourceResolver = effectiveSourceResolver;
        }

        /// <summary>社区/More 屏进入时同时注册顶层导入对话框主机与下载流程导入主机。</summary>
        protected void AcquireRemoteImportHost()
        {
            RemoteImportManager.RegisterHost(this);
            DownloadFlowManager.Instance.SetRemoteImportHost(this);
        }

        /// <summary>本地屏进入时只接管下载流程导入主机（顶层导入对话框主机仍归社区/More 屏）。</summary>
        protected void SetDownloadFlowRemoteImportHost(bool active)
        {
            if (active)
            {
                DownloadFlowManager.Instance.SetRemoteImportHost(this);
            }
            else
            {
                DownloadFlowManager.Instance.ReleaseRemoteImportHost(this);
            }
        }

        /// <summary>
        /// 离开屏幕时保存下载队列并按持有权清空重启返回委托；
        /// releaseRemoteImportHost 的屏幕（社区/More/本地）还注销导入主机。
        /// </summary>
        protected void ReleaseDownloadFlowCallbacks(bool releaseRemoteImportHost)
        {
            DownloadFlowManager downloadManager = DownloadFlowManager.Instance;
            downloadManager.SaveQueue();
            // 单槽委托：只在自己仍持有时清空（ScreensManager 的 Enter/Leave 顺序不保证相邻帧配对）。
            if (downloadManager.RestartReturnRememberer == (Action)RememberRestartReturnToCurrentPage)
            {
                downloadManager.RestartReturnRememberer = null;
            }
            downloadManager.ReleaseRemoteImportHost(this);
            if (releaseRemoteImportHost)
            {
                RemoteImportManager.UnregisterHost(this);
            }
        }
    }
}
