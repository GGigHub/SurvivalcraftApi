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
    /// - RestartReturnRememberer → 重启前记录返回目标页
    /// - SourceModeResolver / EffectiveSourceResolver → 下载源设置由屏幕侧提供
    /// 控制器自持下载进度对话框与队列对话框（DialogsManager 全局），Toast 走全局 Toast 管理器。
    /// </summary>
    internal sealed partial class DownloadFlowManager : IDownloadQueueDialogHost
    {
        public static readonly DownloadFlowManager Instance = new();


        private readonly DownloadQueueManager m_downloadQueue =
            new(TransferManager.DownloadQueuePersistence);
        private readonly ModpackItemImporter m_modpackItemImporter;
        private readonly ModpackDownloader m_modpackDownloader;
        private IRemoteImportHost m_remoteImportHost;

        private Task<FileDownloadResult> m_fileDownloadTask;
        private TransferProgress m_fileDownloadProgress;
        private DownloadDialog m_fileDownloadDialog;
        // 队列覆盖确认框跟踪：切屏使对话框随宿主屏失联时，Update 泵据此重弹（ReshowLostQueuedOverwriteDialog）。
        private ImportOverwriteDialog m_queuedOverwriteDialog;
        private DownloadQueueItem m_queuedOverwriteDialogItem;
        // true = 已对失联对话框发出 Hide，等引擎关闭动画摘除后再重 Show。
        private bool m_queuedOverwriteDialogHidePending;
        private readonly ConditionalWeakTable<DownloadQueueItem, DownloadQueueDialogItemCache> m_downloadQueueDialogItemCache = new();
        private Task<ModpackManifestLoadResult> m_modpackManifestLoadTask;
        private Task<ModpackDownloadProgress> m_modpackItemDownloadTask;
        private Action<ModpackDownloadProgress> m_modpackDownloadCompletedAction;
        private bool m_modpackAllowStartAfterDownloadFailures;
        private int m_modpackDownloadRequestId;
        private bool m_shouldShowRestoredDownloadPromptOnEnter;
        private int m_restoredUnfinishedDownloadCount;

        private List<DownloadQueueItem> m_downloadQueueItems => m_downloadQueue.Items;

        private DownloadQueueItem m_currentDownloadItem
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
                ModIdentity.PackageName,
                () => m_remoteImportHost);
            m_modpackDownloader = new ModpackDownloader(
                TransferManager.ModpackSourceResolver,
                m_modpackItemImporter,
                requestId => m_modpackDownloadRequestId == requestId);
        }

        /// <summary>文件下载（含导入）成功结束后触发；UI 线程。订阅方按 result.Type 刷新本地列表。</summary>
        public event Action<FileDownloadResult> DownloadCompleted;

        /// <summary>整合包子项处理结束后触发；UI 线程。订阅方刷新各类型本地列表。</summary>
        public event Action PackDownloadCompleted;

        /// <summary>状态栏输出口（About Screen 持久状态标签）；屏幕注入。</summary>
        public Action<string> StatusReporter { get; set; }

        /// <summary>重启前记录返回目标页的回调；屏幕注入（各屏记录自身页身份）。</summary>
        public Action RestartReturnRememberer { get; set; }

        /// <summary>下载源选择模式；屏幕按设置提供。</summary>
        public Func<DownloadSourceSelectionMode> SourceModeResolver { get; set; }

        /// <summary>有效下载源解析；屏幕按设置提供。</summary>
        public Func<DownloadSourceOption> EffectiveSourceResolver { get; set; }

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
            GlobalToastManager.Show(message, autoHideSeconds: autoHideSeconds);
        }

        private DownloadSourceSelectionMode SourceMode => SourceModeResolver?.Invoke() ?? DownloadSourceSelectionMode.Default;

        private DownloadSourceOption EffectiveSource => EffectiveSourceResolver?.Invoke() ?? DownloadSourceOption.DefaultCn;

        public void StartDownload(PostFileItem file, PostVersionItem version = null)
        {
            if (file.Type == CommunityFileTypes.ModpackFileType)
            {
                StartModpackDownload(file);
                return;
            }
            ExternalContentType type = CommunityFileRules.MapFileTypeToExternalContentType(file.Type);
            string fileName = CommunityFileRules.GetDownloadFileName(file);
            if (type == ExternalContentType.Mod && version != null)
            {
                LocalModStateStore.RememberPendingLocalModCompatibilityOverride(file.PackageName, fileName, version);
            }
            DownloadFeedbackInfo feedbackInfo = new()
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
                () => ReportStatus(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadFlowManager", "Text", "DownloadCanceled"), fileName)),
                type == ExternalContentType.FurniturePack);
                return;
            }

            StartFileDownload(file.Url, fileName, type, requiresRestart: false, feedbackInfo);
        }

        public void StartFileDownload(
            string url,
            string fileName,
            ExternalContentType type,
            bool requiresRestart,
            DownloadFeedbackInfo feedbackInfo = null,
            bool replaceExistingScbbsPlusMod = false,
            string replaceExistingModPackageName = null,
            string replaceExistingModPath = null,
            string overwriteExistingFilePath = null)
        {
            if (SourceMode == DownloadSourceSelectionMode.ManualSelect)
            {
                ShowDownloadSourceSelectionDialog(
                    source => EnqueueFileDownload(
                        url,
                        fileName,
                        type,
                        requiresRestart,
                        source,
                        feedbackInfo,
                        replaceExistingScbbsPlusMod,
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
                replaceExistingScbbsPlusMod,
                replaceExistingModPackageName,
                replaceExistingModPath,
                overwriteExistingFilePath);
        }

        public void EnqueueFileDownload(
            string url,
            string fileName,
            ExternalContentType type,
            bool requiresRestart,
            DownloadSourceOption source,
            DownloadFeedbackInfo feedbackInfo = null,
            bool replaceExistingScbbsPlusMod = false,
            string replaceExistingModPackageName = null,
            string replaceExistingModPath = null,
            string overwriteExistingFilePath = null)
        {
            string downloadUrl = DownloadSourceResolver.ResolveUrl(url, source);
            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                DialogsManager.ShowDialog(null, new MessageDialog(LanguageControl.Error, LanguageControl.Get("ScbbsPlusCommunityScreen", "Community", "Posts", "DownloadSourceSwitchUnavailable"), LanguageControl.Ok, null, null));
                return;
            }

            DownloadQueueItem item = m_downloadQueue.Enqueue(
                downloadUrl,
                url,
                fileName,
                type,
                requiresRestart,
                source,
                feedbackInfo,
                replaceExistingScbbsPlusMod,
                replaceExistingModPackageName,
                replaceExistingModPath,
                overwriteExistingFilePath);
            if (item == null)
            {
                ReportStatus(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusCommunityScreen", "Community", "Posts", "DuplicateDownloadQueued"), fileName));
                return;
            }
            SaveQueue();
            ShowToast(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusCommunityScreen", "Community", "Posts", "StartDownloading"), fileName), autoHideSeconds: 2.0);
            ReportStatus(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusCommunityScreen", "Community", "Posts", "AddedToDownloadQueue"), fileName));
            StartNextQueuedDownload();
        }

        public void ShowDownloadSourceSelectionDialog(Action<DownloadSourceOption> selectSourceAction)
        {
            DownloadSourceOption[] sourceOptions = Enum.GetValues<DownloadSourceOption>();
            DialogsManager.ShowDialog(
                null,
                new ListSelectionDialog(
                    LanguageControl.Get("ScbbsPlusCommunityScreen", "Community", "Posts", "ChooseDownloadSource"),
                    sourceOptions,
                    60f,
                    item => GetDownloadSourceText((DownloadSourceOption)item),
                    item => selectSourceAction?.Invoke((DownloadSourceOption)item)));
        }

    }
}