using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Engine;

namespace Game
{
    /// <summary>
    /// 下载与整合包管线的进程级控制器：文件下载队列、进度对话框、整合包清单/子项下载。
    /// 三屏共用；界面副作用通过事件与注入回调输出：
    /// - DownloadCompleted / PackDownloadCompleted → 订阅方刷新本地列表
    /// - StatusReporter → 订阅方写状态栏（关于对话框）
    /// - ManualSelectResolver / EffectiveSourceResolver → 下载源设置由屏幕侧提供
    /// 控制器自持下载进度对话框与队列对话框（DialogsManager 全局），Toast 走全局 Toast 管理器。
    /// </summary>
    internal sealed partial class DownloadFlowManager : IDownloadQueueDialogHost
    {
        public static readonly DownloadFlowManager Instance = new();


        private readonly DownloadQueueManager m_downloadQueue =
            new(TransferManager.CommunityDownloadQueuePersistence);
        private readonly ModpackItemImporter m_modpackItemImporter;
        private readonly ModpackDownloader m_modpackDownloader;
        private IRemoteImportHost m_remoteImportHost;

        private Task<CommunityFileDownloadResult> m_fileDownloadTask;
        private CommunityTransferProgress m_fileDownloadProgress;
        private CommunityDownloadDialog m_fileDownloadDialog;
        // 队列覆盖确认框跟踪：切屏使对话框随宿主屏失联时，Update 泵据此重弹（ReshowLostQueuedOverwriteDialog）。
        private CommunityImportOverwriteDialog m_queuedOverwriteDialog;
        private CommunityDownloadQueueItem m_queuedOverwriteDialogItem;
        // true = 已对失联对话框发出 Hide，等引擎关闭动画摘除后再重 Show。
        private bool m_queuedOverwriteDialogHidePending;
        private readonly ConditionalWeakTable<CommunityDownloadQueueItem, DownloadQueueDialogItemCache> m_downloadQueueDialogItemCache = new();
        private Task<ModpackManifestLoadResult> m_modpackManifestLoadTask;
        private Task<ModpackDownloadProgress> m_modpackItemDownloadTask;
        private Action<ModpackDownloadProgress> m_modpackDownloadCompletedAction;
        private bool m_modpackAllowStartAfterDownloadFailures;
        private int m_modpackDownloadRequestId;
        private bool m_shouldShowRestoredDownloadPromptOnEnter;
        private int m_restoredUnfinishedDownloadCount;

        private List<CommunityDownloadQueueItem> m_downloadQueueItems => m_downloadQueue.Items;

        private CommunityDownloadQueueItem m_currentDownloadItem
        {
            get => m_downloadQueue.Current;
            set
            {
                if (value == null)
                {
                    m_downloadQueue.ClearCurrent();
                }
                else
                {
                    m_downloadQueue.SetCurrent(value);
                }
            }
        }

        private DownloadFlowManager()
        {
            m_modpackItemImporter = new ModpackItemImporter(
                () => m_remoteImportHost);
            m_modpackDownloader = new ModpackDownloader(
                TransferManager.ModpackSourceResolver,
                m_modpackItemImporter,
                requestId => m_modpackDownloadRequestId == requestId);
        }

        /// <summary>文件下载（含导入）成功结束后触发；UI 线程。订阅方按 result.Type 刷新本地列表。</summary>
        public event Action<CommunityFileDownloadResult> DownloadCompleted;

        /// <summary>整合包子项处理结束后触发；UI 线程。订阅方刷新各类型本地列表。</summary>
        public event Action PackDownloadCompleted;

        /// <summary>状态栏输出口（About Screen 持久状态标签）；屏幕注入。</summary>
        public Action<string> StatusReporter { get; set; }

        /// <summary>每次手动选择下载源（true = 下载时弹框选源）；屏幕按设置提供。</summary>
        public Func<bool> ManualSelectResolver { get; set; }

        /// <summary>有效下载源解析；屏幕按设置提供。</summary>
        public Func<CommunityDownloadSourceManager.Info> EffectiveSourceResolver { get; set; }

        /// <summary>整合包清单/子项任务进行中。</summary>
        public bool ModpackDownloadBusy => m_modpackManifestLoadTask != null || m_modpackItemDownloadTask != null;

        /// <summary>任一下载管线任务进行中。</summary>
        public bool IsBusy => ModpackDownloadBusy || m_fileDownloadTask != null || m_currentDownloadItem != null;

        /// <summary>当前屏的远程导入宿（已存在判断、导入回调等）；各屏 Enter 时设置、Leave 时清空。</summary>
        public void SetRemoteImportHost(IRemoteImportHost host)
        {
            m_remoteImportHost = host;
        }

        public void ReleaseRemoteImportHost(IRemoteImportHost host)
        {
            // 单槽委托只在自己仍持有时清空：ScreensManager 的 Enter/Leave 不保证相邻帧配对，
            // 迟到的释放若无条件置 null，会抹掉后进入屏幕刚注册的 host——下载中
            // IsModpackDataFilePresent 恒 false、ImportModpackDataFileSafe 变 no-op，整合包管线
            // 却照常计成功（host 缺失的最主要触发源）。
            if (ReferenceEquals(m_remoteImportHost, host))
            {
                m_remoteImportHost = null;
            }
        }

        private void ReportStatus(string message)
        {
            StatusReporter?.Invoke(message);
        }

        private void ShowToast(string message, double autoHideSeconds = 0.0)
        {
            CommunityToastManager.Show(message, autoHideSeconds: autoHideSeconds);
        }

        private bool ManualSelect => ManualSelectResolver?.Invoke() ?? false;

        private CommunityDownloadSourceManager.Info EffectiveSource => EffectiveSourceResolver?.Invoke() ?? CommunityDownloadSourceManager.DefaultCnInfo;

        public void StartDownload(PostFileItem file, PostVersionItem version = null)
        {
            if (file.Type == CommunityFileTypes.ModpackFileType)
            {
                StartModpackDownload(file);
                return;
            }
            ExternalContentType type = CommunityFileTypes.ToExternal(file.Type);
            string fileName = CommunityFileRules.GetDownloadFileName(file);
            if (CommunityFileTypes.IsModLike(type) && version != null)
            {
                LocalModStateStore.RememberPendingLocalModCompatibilityOverride(file.PackageName, fileName, version);
            }
            CommunityDownloadFeedbackInfo feedbackInfo = new()
            {
                ApiBaseUrl = AuthSessionManager.Instance.ApiBaseUrl,
                Token = AuthSessionManager.Instance.CurrentToken,
                FileId = file.Id,
                PostId = file.PostId,
                Platform = GetPlatformName(),
                Version = VersionCatalogCache.CurrentApiVersion
            };
            string existingPath = LocalModStateStore.FindExactLocalDownloadFilePath(type, fileName);
            if (!string.IsNullOrWhiteSpace(existingPath))
            {
                ShowDownloadOverwriteDialog(
                    fileName,
                    () => StartFileDownload(file.Url, fileName, type, requiresRestart: false, feedbackInfo, overwriteExistingFilePath: existingPath),
                    () => StartFileDownload(file.Url, fileName, type, requiresRestart: false, feedbackInfo),
                // 仅家具包提供"导入为副本"（引擎重名自动另起新名保留旧包），见覆盖确认调用处。
                () => ReportStatus(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("DownloadFlowManager", "DownloadCanceled"), fileName)),
                type == ExternalContentType.FurniturePack);
                return;
            }

            // 同包名但文件名不同：Mods 目录本就允许同包名多副本共存（引擎仅启用其一，其余手动禁用）。
            // 恰有一个同包名模组时自动禁用旧版（持久化到 DisabledMods：重启后旧版失效、新版生效，
            // 本进程旧版仍在运行不受影响，管理屏立即显示黄字"已禁用"）；已有多个副本时直接下载，
            // 由用户自行管理启用。不走"替换并删除旧文件"的更新链——链接导入链保留该语义。
            if (CommunityFileTypes.IsModLike(type)
                && !string.IsNullOrWhiteSpace(file.PackageName)
                // 包名来自社区数据，挡掉内置模组名，防止服务端恶意/损坏数据禁用核心模组
                && file.PackageName is not ("survivalcraft" or "fastdebug"))
            {
                List<ModEntity> samePackage = ModsManager.ModListAll
                    .Where(entity => entity?.modInfo != null
                        && string.Equals(entity.modInfo.PackageName, file.PackageName, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (samePackage.Count == 1
                    && !string.IsNullOrWhiteSpace(samePackage[0].modInfo.Version))
                {
                    ModEntity oldVersion = samePackage[0];
                    // 禁用键用 modinfo 侧包名：DisabledMods 字典大小写敏感，社区侧包名与
                    // 本地 modinfo 大小写不一致时禁用会静默失效
                    PendingLocalModActions.SetManualDisabledState(oldVersion.modInfo.PackageName, oldVersion.modInfo.Version, isDisabled: true);
                    ShowToast(string.Format(CultureInfo.InvariantCulture,
                        LanguageControl.Get("DownloadFlowManager", "OldVersionDisabledPendingRestart"),
                        Storage.GetFileName(oldVersion.ModFilePath)), autoHideSeconds: 3.0);
                }
            }

            StartFileDownload(file.Url, fileName, type, requiresRestart: false, feedbackInfo);
        }

        public void StartFileDownload(
            string url,
            string fileName,
            ExternalContentType type,
            bool requiresRestart,
            CommunityDownloadFeedbackInfo feedbackInfo = null,
            string replaceExistingModPackageName = null,
            string replaceExistingModPath = null,
            string overwriteExistingFilePath = null)
        {
            if (ManualSelect)
            {
                ShowDownloadSourceSelectionDialog(
                    source => EnqueueFileDownload(
                        url,
                        fileName,
                        type,
                        requiresRestart,
                        source,
                        feedbackInfo,
                        replaceExistingModPackageName,
                        replaceExistingModPath,
                        overwriteExistingFilePath));
                return;
            }

            EnqueueFileDownload(
                url,
                fileName,
                type,
                requiresRestart,
                EffectiveSource,
                feedbackInfo,
                replaceExistingModPackageName,
                replaceExistingModPath,
                overwriteExistingFilePath);
        }

        public void EnqueueFileDownload(
            string url,
            string fileName,
            ExternalContentType type,
            bool requiresRestart,
            CommunityDownloadSourceManager.Info source,
            CommunityDownloadFeedbackInfo feedbackInfo = null,
            string replaceExistingModPackageName = null,
            string replaceExistingModPath = null,
            string overwriteExistingFilePath = null)
        {
            string downloadUrl = CommunityDownloadSourceResolver.ResolveUrl(url, source);
            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                DialogsManager.ShowDialog(null, new MessageDialog(LanguageControl.Error, LanguageControl.Get("DownloadFlowManager", "DownloadSourceSwitchUnavailable"), LanguageControl.Ok, null, null));
                return;
            }

            CommunityDownloadQueueItem item = m_downloadQueue.Enqueue(
                downloadUrl,
                url,
                fileName,
                type,
                requiresRestart,
                source,
                feedbackInfo,
                replaceExistingModPackageName,
                replaceExistingModPath,
                overwriteExistingFilePath);
            if (item == null)
            {
                ReportStatus(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("DownloadFlowManager", "DuplicateDownloadQueued"), fileName));
                return;
            }
            SaveQueue();
            ShowToast(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("DownloadFlowManager", "StartDownloading"), fileName), autoHideSeconds: 2.0);
            ReportStatus(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("DownloadFlowManager", "AddedToDownloadQueue"), fileName));
            StartNextQueuedDownload();
        }

        public void ShowDownloadSourceSelectionDialog(Action<CommunityDownloadSourceManager.Info> selectSourceAction)
        {
            DialogsManager.ShowDialog(
                null,
                new ListSelectionDialog(
                    LanguageControl.Get("DownloadFlowManager", "ChooseDownloadSource"),
                    CommunityDownloadSourceManager.GetAllInfos(),
                    60f,
                    item => CommunityDownloadSourceManager.GetDisplayName((CommunityDownloadSourceManager.Info)item),
                    item => selectSourceAction?.Invoke((CommunityDownloadSourceManager.Info)item)));
        }

    }
}