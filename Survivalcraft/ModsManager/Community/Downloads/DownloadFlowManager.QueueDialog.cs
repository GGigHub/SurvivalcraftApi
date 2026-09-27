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
    internal sealed partial class DownloadFlowManager
    {
        private void StartNextQueuedDownload()
        {
            if (m_fileDownloadTask != null || m_currentDownloadItem != null)
            {
                return;
            }

            if (m_downloadQueue.HasAwaitingOverwriteConfirmation())
            {
                return;
            }

            CommunityDownloadQueueItem item = m_downloadQueue.FindNextPending();
            if (item == null)
            {
                return;
            }

            if (TryRequestQueuedDownloadOverwrite(item))
            {
                return;
            }

            CancellationToken cancellationToken = m_downloadQueue.Begin(item, DateTime.Now);
            m_fileDownloadProgress = item.Progress;
            item.Progress.Set(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadFlowManager", "DownloadingFormat"), item.FileName), 0f, true);
            SaveQueue();
            ReportStatus(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadFlowManager", "DownloadingFormat"), item.FileName));
            ShowQueuedDownloadProgressDialog(item);
            m_fileDownloadTask = Task.Run(async () =>
            {
                return await ExecuteQueuedDownloadAsync(
                    TransferManager.CommunityHttpDownloader,
                    TransferManager.CommunityDownloadFeedbackClient,
                    item,
                    cancellationToken,
                    CreateDownloadProgressReporter(item),
                    stream =>
                    {
                        item.Progress.Set(LanguageControl.Get("ScbbsPlusDownloadFlowManager", "Importing"), 1f, true);
                        // 队列项字段拼装准备结果后统一走 ExternalContentManager.ImportPreparedExternalContent
                        // 决策树（自更新/覆盖更新/覆盖回滚/普通导入），与链接导入共用一套分支口径。
                        ExternalContentPreparation preparation = new()
                        {
                            Type = item.Type,
                            FileName = item.FileName,
                            ExistingModPackageName = item.ReplaceExistingScbbsPlusMod
                                ? ModIdentity.PackageName
                                : item.ReplaceExistingModPackageName,
                            ExistingModPath = item.ReplaceExistingScbbsPlusMod
                                ? item.OverwriteExistingFilePath
                                : item.ReplaceExistingModPath,
                            ExistingEntryFilePath = item.OverwriteExistingFilePath
                        };
                        return ExternalContentManager.ImportPreparedExternalContent(ModIdentity.PackageName, stream, preparation, overwriteExisting: true);
                    },
                    onImportCompleted: () =>
                    {
                        // 后台线程当场置位并落盘：进程在收尾前死亡时，恢复流程据此把
                        // 该项改判 Completed，不再重复导入。
                        item.Imported = true;
                        item.CompletedTime ??= DateTime.Now;
                        SaveQueue();
                    });
            });
        }

        private bool TryRequestQueuedDownloadOverwrite(CommunityDownloadQueueItem item)
        {
            if (item == null
                || item.ReplaceExistingScbbsPlusMod
                || !string.IsNullOrWhiteSpace(item.ReplaceExistingModPackageName)
                || !string.IsNullOrWhiteSpace(item.OverwriteExistingFilePath))
            {
                return false;
            }

            string existingPath = LocalModStateStore.FindExactLocalDownloadFilePath(item.Type, item.FileName);
            if (string.IsNullOrWhiteSpace(existingPath))
            {
                return false;
            }

            return BeginQueuedDownloadOverwriteConfirmation(item, existingPath, preConfirm: null);
        }

        private void ConfirmQueuedDownloadOverwrite(CommunityDownloadQueueItem item, string existingPath)
        {
            if (item == null || item.State != CommunityDownloadQueueItemState.AwaitingOverwriteConfirmation)
            {
                return;
            }

            m_downloadQueue.ConfirmOverwrite(item, existingPath);
            item.Progress?.Set(LanguageControl.Get("ScbbsPlusDownloadFlowManager", "Queued"), 0f, false);
            SaveQueue();
            StartNextQueuedDownload();
        }

        private void CancelQueuedDownloadOverwrite(CommunityDownloadQueueItem item)
        {
            if (item == null || item.State != CommunityDownloadQueueItemState.AwaitingOverwriteConfirmation)
            {
                return;
            }

            m_downloadQueue.CancelOverwrite(item, DateTime.Now);
            item.Error = string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadFlowManager", "DownloadCanceled"), item.FileName);
            item.Progress?.Set(item.Error, 0f, false);
            SaveQueue();
            ReportStatus(item.Error);
            StartNextQueuedDownload();
        }

        private void RenameQueuedDownloadOverwrite(CommunityDownloadQueueItem item)
        {
            if (item == null || item.State != CommunityDownloadQueueItemState.AwaitingOverwriteConfirmation)
            {
                return;
            }

            // 不设覆盖路径 → 按游戏原生导入（World 另建目录、FurniturePack 去重），保留旧项。
            m_downloadQueue.ConfirmOverwrite(item, null);
            item.Progress?.Set(LanguageControl.Get("ScbbsPlusDownloadFlowManager", "Queued"), 0f, false);
            SaveQueue();
            StartNextQueuedDownload();
        }

        /// <summary>
        ///     收尾当前队列项。返回 true 表示该项已就「文件已存在」弹出覆盖确认并重排队：
        ///     错误属预期流程，调用方须跳过后续终态 UI（失败框等），否则会叠出两个对话框。
        /// </summary>
        private bool CompleteCurrentDownloadItem(CommunityFileDownloadResult result)
        {
            CommunityDownloadQueueItem item = m_currentDownloadItem;
            if (item == null)
            {
                return false;
            }

            item.CompletedTime = DateTime.Now;
            item.Result = result;
            item.CancelSource?.Dispose();
            item.CancelSource = null;
            item.PauseRequested = false;
            if (result?.Paused == true)
            {
                item.State = CommunityDownloadQueueStateMachine.Transition(item.State, CommunityDownloadQueueCommand.Pause);
                item.Error = null;
                item.CompletedTime = null;
                item.Progress.Set(
                    result.Error ?? string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadFlowManager", "DownloadPaused"), item.FileName),
                    GetResumeProgressValue(item.TempFilePath),
                    false);
            }
            else if (result?.Canceled == true)
            {
                item.State = CommunityDownloadQueueStateMachine.Transition(item.State, CommunityDownloadQueueCommand.Cancel);
                item.Error = result.Error ?? string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadFlowManager", "DownloadCanceled"), item.FileName);
                item.Progress.Set(item.Error, 0f, false);
            }
            else if (string.IsNullOrEmpty(result?.Error))
            {
                item.State = CommunityDownloadQueueStateMachine.Transition(item.State, CommunityDownloadQueueCommand.Complete);
                item.Progress.Set(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadFlowManager", "Imported"), item.FileName), 1f, true);
                DeleteDownloadTempFile(item);
            }
            else
            {
                if (TryRequeueDownloadOverwriteAfterExistingFileError(item, result))
                {
                    return true;
                }

                item.State = CommunityDownloadQueueStateMachine.Transition(item.State, CommunityDownloadQueueCommand.Fail);
                item.Error = result.Error;
                item.Progress.Set(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        result.DownloadFailed
                            ? LanguageControl.Get("ScbbsPlusDownloadFlowManager", "DownloadFailed")
                            : LanguageControl.Get("ScbbsPlusDownloadFlowManager", "ImportFailed"),
                        result.Error),
                    0f,
                    false);
                Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadFlowManager", "BackgroundTaskFailed"), item.FileName, result.DownloadFailed, result.Error));
            }
            m_currentDownloadItem = null;
            SaveQueue();
            return false;
        }

        /// <summary>
        /// 两条覆盖确认路径（队列启动前预检 / 「文件已存在」错误重排队）共用的进入段：
        /// 置确认态、挂起进度、弹三选对话框。差异动作经 preConfirm 注入（重排队路径
        /// 借此清掉失败残留的 Result/Error 并释放当前项引用）。两语境下
        /// m_currentDownloadItem 均应为 null（预检时尚未启动、重排队时收尾释放）。
        /// </summary>
        private bool BeginQueuedDownloadOverwriteConfirmation(CommunityDownloadQueueItem item, string existingPath, Action preConfirm)
        {
            if (item == null || string.IsNullOrWhiteSpace(existingPath))
            {
                return false;
            }

            m_downloadQueue.RequestOverwriteConfirmation(item);
            preConfirm?.Invoke();
            item.Progress?.Set(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadFlowManager", "AlreadyExistsReplace"), item.FileName), 0f, false);
            m_currentDownloadItem = null;
            SaveQueue();
            ReportStatus(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadFlowManager", "AlreadyExistsReplace"), item.FileName));
            m_queuedOverwriteDialogItem = item;
            // 换绑新框时清掉旧框的 Hide 待重挂标志：否则同帧内"取消旧框→立即开始下一项→
            // 新框又失联"时，残留的 true 会压制对旧实例的 HideDialog，重挂等不到旧框出册。
            m_queuedOverwriteDialogHidePending = false;
            m_queuedOverwriteDialog = ShowDownloadOverwriteDialog(
                item.FileName,
                () => ConfirmQueuedDownloadOverwrite(item, existingPath),
                () => RenameQueuedDownloadOverwrite(item),
                // 仅家具包提供"导入为副本"（引擎重名自动另起新名保留旧包），见覆盖确认调用处。
                () => CancelQueuedDownloadOverwrite(item),
                item.Type == ExternalContentType.FurniturePack);
            return true;
        }

        // TODO: 游戏本体下个版本会添加对话框隐藏、显示钩子，需优化模组轮询代码
        /// <summary>
        /// 泵每帧核对等待覆盖确认的队列项其对话框是否仍可见。引擎切屏只把旧屏从 RootWidget
        /// 摘除，对话框实例仍留在 DialogsManager.m_dialogs（挂在死屏上），故单查 Dialogs 判不出
        /// 失联，存活判据须为「在 Dialogs 中且父链可达 RootWidget」。失联后先 Hide 再重挂，
        /// 重挂条件为「已移出 Dialogs 且 m_animationData 不再含该实例」：HideDialog 立即
        /// 摘除在册关系（泵在 Window.Frame 主线程，Dispatch 内联生效），但旧 CoverWidget（全屏模态暗幕）要等关闭动画条目 Factor≤0 时才由
        /// DialogsManager.Update 摘下并移除条目（Factor 以 6/秒递减，最迟约 1/6 秒，无死等）；
        /// 若条目未清就重挂，ShowDialog 写入的新条目会覆盖旧条目，旧 CoverWidget 从此无人
        /// 摘除，孤立在死屏上（无用户可见后果，但钉住死屏 Children）。不走
        /// RequestOverwriteConfirmation，该项状态已迁移，重入会抛 InvalidOperationException；
        /// 回调闭包引用的 item/existingPath 不变，无需重建对话框。
        /// </summary>
        private void ReshowLostQueuedOverwriteDialog()
        {
            CommunityDownloadQueueItem item = m_queuedOverwriteDialogItem;
            if (item == null || item.State != CommunityDownloadQueueItemState.AwaitingOverwriteConfirmation)
            {
                m_queuedOverwriteDialog = null;
                m_queuedOverwriteDialogItem = null;
                m_queuedOverwriteDialogHidePending = false;
                return;
            }

            CommunityImportOverwriteDialog dialog = m_queuedOverwriteDialog;
            if (dialog == null)
            {
                return;
            }

            if (DialogsManager.Dialogs.Contains(dialog))
            {
                if (IsWidgetInRootHierarchy(dialog))
                {
                    m_queuedOverwriteDialogHidePending = false;
                    return;
                }
                if (!m_queuedOverwriteDialogHidePending)
                {
                    DialogsManager.HideDialog(dialog);
                    m_queuedOverwriteDialogHidePending = true;
                }
                return;
            }

            if (DialogsManager.m_animationData.ContainsKey(dialog))
            {
                // 旧关闭动画条目未清（DialogsManager.Update 尚未在 Factor≤0 时摘下旧
                // CoverWidget），此时重挂会覆盖条目致其孤立，继续等待下帧复评。
                return;
            }

            DialogsManager.ShowDialog(null, dialog);
            m_queuedOverwriteDialogHidePending = false;
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

        private bool TryRequeueDownloadOverwriteAfterExistingFileError(CommunityDownloadQueueItem item, CommunityFileDownloadResult result)
        {
            if (item == null
                || result == null
                || string.IsNullOrWhiteSpace(result.ExistingFilePath)
                || item.ReplaceExistingScbbsPlusMod
                || !string.IsNullOrWhiteSpace(item.ReplaceExistingModPackageName)
                || !string.IsNullOrWhiteSpace(item.OverwriteExistingFilePath))
            {
                return false;
            }
            // Pausing/Canceling 等非 Pending/Running 态无法走覆盖确认重排队；回落到状态机的 Fail 转换，
            // 避免 RequestOverwriteConfirmation 的守卫在这些态抛异常冒泡出 Update。
            if (item.State is not (CommunityDownloadQueueItemState.Pending or CommunityDownloadQueueItemState.Running))
            {
                return false;
            }

            // 引擎异常携带的路径是权威来源；本地精确查找仅兜底 Path 为空的个别 throw 路径。
            string existingPath = result.ExistingFilePath;
            if (string.IsNullOrWhiteSpace(existingPath))
            {
                existingPath = LocalModStateStore.FindExactLocalDownloadFilePath(item.Type, item.FileName);
            }
            if (string.IsNullOrWhiteSpace(existingPath))
            {
                return false;
            }

            return BeginQueuedDownloadOverwriteConfirmation(item, existingPath, preConfirm: () =>
            {
                item.Result = null;
                item.Error = null;
            });
        }

        private static CommunityImportOverwriteDialog ShowDownloadOverwriteDialog(string fileName, Action confirmAction, Action renameAction, Action cancelAction, bool canRename)
        {
            string message = string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadFlowManager", "LocalAlreadyExistsChooseAction"), fileName);
            CommunityImportOverwriteDialog dialog = new(
                message,
                canRename,
                action =>
                {
                    switch (action)
                    {
                        case ImportOverwriteAction.Overwrite:
                            confirmAction?.Invoke();
                            break;
                        case ImportOverwriteAction.Rename:
                            renameAction?.Invoke();
                            break;
                        case ImportOverwriteAction.Cancel:
                            cancelAction?.Invoke();
                            break;
                    }
                });
            DialogsManager.ShowDialog(null, dialog);
            return dialog;
        }

        public void LoadQueue()
        {
            m_shouldShowRestoredDownloadPromptOnEnter = false;
            m_restoredUnfinishedDownloadCount = 0;
            if (!m_downloadQueue.Load(SetRestoredDownloadQueueProgress))
            {
                return;
            }

            m_restoredUnfinishedDownloadCount = m_downloadQueueItems.Count(item => !IsFinishedDownloadQueueItem(item));
            m_shouldShowRestoredDownloadPromptOnEnter = m_restoredUnfinishedDownloadCount > 0;
            Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadFlowManager", "DownloadQueueRestored"), m_downloadQueueItems.Count));
        }

        public void SaveQueue()
        {
            try
            {
                m_downloadQueue.Save();
            }
            catch (Exception ex)
            {
                Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadFlowManager", "SaveDownloadQueueFailed"), ex.Message));
            }
        }

        public void TryShowRestoredDownloadPromptOnEnter()
        {
            if (!m_shouldShowRestoredDownloadPromptOnEnter || m_restoredUnfinishedDownloadCount <= 0)
            {
                return;
            }

            m_shouldShowRestoredDownloadPromptOnEnter = false;
            CommunityToastManager.Show(
                string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadFlowManager", "UnfinishedDownloadsBadge"), m_restoredUnfinishedDownloadCount),
                confirmAction: ShowQueueDialog,
                dismissAction: () => m_restoredUnfinishedDownloadCount = 0);
        }

        public void ShowQueueDialog()
        {
            foreach (Dialog dialog in DialogsManager.Dialogs)
            {
                if (dialog is CommunityDownloadQueueDialog)
                {
                    return;
                }
            }

            DialogsManager.ShowDialog(null, new CommunityDownloadQueueDialog(this, null));
        }

        // IDownloadQueueDialogHost：队列对话框的宿主实现，委托到既有私有方法。
        IReadOnlyList<CommunityDownloadQueueDialog.DownloadQueueDialogItem> IDownloadQueueDialogHost.GetQueueDialogItems()
            => BuildDownloadQueueDialogItems();

        void IDownloadQueueDialogHost.PauseOrResumeDownloadQueueItem(int? itemId)
            => PauseOrResumeDownloadQueueItem(itemId);

        void IDownloadQueueDialogHost.CancelDownloadQueueItem(int? itemId)
            => CancelDownloadQueueItem(itemId);

        void IDownloadQueueDialogHost.RetryDownloadQueueItem(int? itemId)
            => RetryDownloadQueueItem(itemId);

        void IDownloadQueueDialogHost.ClearFinishedDownloadQueueItems(int? itemId)
            => ClearFinishedDownloadQueueItems(itemId);

        private void ShowDownloadFailureDialog(CommunityDownloadQueueItem item)
        {
            if (item == null)
            {
                return;
            }

            string error = item.Result?.Error ?? item.Error ?? LanguageControl.Get("ScbbsPlusDownloadFlowManager", "UnknownError");
            DialogsManager.ShowDialog(
                null,
                new CommunityDownloadFailureDialog(
                    item.FileName,
                    item.Url,
                    error,
                    option => SwitchDownloadSourceAndRetry(item, option)));
        }

        private void SwitchDownloadSourceAndRetry(CommunityDownloadQueueItem item, CommunityDownloadSourceOption source)
        {
            if (item == null)
            {
                return;
            }

            string baseUrl = string.IsNullOrWhiteSpace(item.OriginalUrl) ? item.Url : item.OriginalUrl;
            string switchedUrl = CommunityDownloadSourceResolver.ResolveUrl(baseUrl, source);
            if (string.IsNullOrWhiteSpace(switchedUrl))
            {
                DialogsManager.ShowDialog(null, new MessageDialog(LanguageControl.Error, LanguageControl.Get("ScbbsPlusDownloadFlowManager", "DownloadSourceSwitchUnavailable"), LanguageControl.Ok, null, null));
                return;
            }

            item.OriginalUrl = baseUrl;
            item.Url = switchedUrl;
            item.Source = source;
            SaveQueue();
            ReportStatus(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadFlowManager", "SwitchedDownloadSource"), GetDownloadSourceText(source)));
            RetryDownloadQueueItem(item.Id);
        }

        internal static string GetDownloadSourceText(CommunityDownloadSourceOption source)
        {
            return source switch
            {
                CommunityDownloadSourceOption.Source1Global => LanguageControl.Get("ScbbsPlusDownloadFlowManager", "Source1Global"),
                CommunityDownloadSourceOption.Source2Cn => LanguageControl.Get("ScbbsPlusDownloadFlowManager", "Source2MainlandChina"),
                CommunityDownloadSourceOption.Source3Global => LanguageControl.Get("ScbbsPlusDownloadFlowManager", "Source3Global"),
                CommunityDownloadSourceOption.Source4Global => LanguageControl.Get("ScbbsPlusDownloadFlowManager", "Source4Global"),
                _ => LanguageControl.Get("ScbbsPlusDownloadFlowManager", "DefaultMainlandChina")
            };
        }

        private static void SetRestoredDownloadQueueProgress(CommunityDownloadQueueItem item)
        {
            string message = item.State switch
            {
                CommunityDownloadQueueItemState.Pending => LanguageControl.Get("ScbbsPlusDownloadFlowManager", "Queued"),
                CommunityDownloadQueueItemState.Paused => FormatPausedDownloadMessage(item),
                CommunityDownloadQueueItemState.Canceled => item.Error ?? string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadFlowManager", "DownloadCanceled"), item.FileName),
                CommunityDownloadQueueItemState.Failed => item.Error ?? LanguageControl.Get("ScbbsPlusDownloadFlowManager", "Failed"),
                CommunityDownloadQueueItemState.Completed => string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadFlowManager", "Imported"), item.FileName),
                _ => GetDownloadQueueItemStateText(item.State)
            };
            item.Progress?.Set(message, GetResumeProgressValue(item.TempFilePath), false);
        }

        private static string FormatPausedDownloadMessage(CommunityDownloadQueueItem item)
        {
            bool hasDownloaded = Storage.TryGetFileSize(item?.TempFilePath, out long length) && length > 0;
            return hasDownloaded
                ? string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadFlowManager", "DownloadPausedDownloaded"), item.FileName, DataSizeFormatter.Format(length))
                : string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadFlowManager", "DownloadPaused"), item?.FileName);
        }

        private IReadOnlyList<CommunityDownloadQueueDialog.DownloadQueueDialogItem> BuildDownloadQueueDialogItems()
        {
            if (m_downloadQueueItems.Count == 0)
            {
                return Array.Empty<CommunityDownloadQueueDialog.DownloadQueueDialogItem>();
            }

            List<CommunityDownloadQueueDialog.DownloadQueueDialogItem> items = [];
            for (int i = 0; i < m_downloadQueueItems.Count; i++)
            {
                CommunityDownloadQueueItem item = m_downloadQueueItems[i];
                (string message, float fraction, bool hasPercent) = item.Progress?.Snapshot() ?? (string.Empty, 0f, false);
                if (!m_downloadQueueDialogItemCache.TryGetValue(item, out DownloadQueueDialogItemCache cache)
                    || cache.State != item.State
                    || !string.Equals(cache.Message, message, StringComparison.Ordinal)
                    || !string.Equals(cache.Error, item.Error, StringComparison.Ordinal)
                    || cache.Value != fraction
                    || cache.HasPercent != hasPercent)
                {
                    string status = GetDownloadQueueItemStateText(item.State);
                    string percentText = hasPercent ? string.Format(CultureInfo.InvariantCulture, " {0:0}%", fraction * 100f) : string.Empty;
                    string detail = item.State == CommunityDownloadQueueItemState.Failed && !string.IsNullOrWhiteSpace(item.Error)
                        ? item.Error
                        : message;
                    if (cache == null)
                    {
                        cache = new DownloadQueueDialogItemCache();
                        m_downloadQueueDialogItemCache.Add(item, cache);
                    }
                    cache.State = item.State;
                    cache.Message = message;
                    cache.Error = item.Error;
                    cache.Value = fraction;
                    cache.HasPercent = hasPercent;
                    cache.Item = new CommunityDownloadQueueDialog.DownloadQueueDialogItem
                    {
                        Id = item.Id,
                        Title = string.Format(CultureInfo.InvariantCulture, "{0}. [{1}{2}] {3}", item.Id, status, percentText, item.FileName),
                        Detail = string.IsNullOrWhiteSpace(detail) ? status : detail,
                        // 与 PauseOrResume/Cancel/Retry/ClearFinished 四个操作的可操作性判定保持一致
                        CanPauseResume = item.State is CommunityDownloadQueueItemState.Running or CommunityDownloadQueueItemState.Paused,
                        CanCancel = item.State is CommunityDownloadQueueItemState.Running
                            or CommunityDownloadQueueItemState.Pausing
                            or CommunityDownloadQueueItemState.Pending
                            or CommunityDownloadQueueItemState.AwaitingOverwriteConfirmation
                            or CommunityDownloadQueueItemState.Paused,
                        CanRetry = item.State is CommunityDownloadQueueItemState.Failed
                            or CommunityDownloadQueueItemState.Canceled
                            or CommunityDownloadQueueItemState.Paused,
                        CanClear = IsFinishedDownloadQueueItem(item)
                    };
                }
                items.Add(cache.Item);
            }
            return items;
        }

        // 队列对话框每帧轮询时避免重复构建展示字符串：输入（状态/消息/进度/错误）不变则复用缓存实例。
        // 条目通过 ConditionalWeakTable 挂在队列项上，随队列项移除并回收，无需手动清理。
        private sealed class DownloadQueueDialogItemCache
        {
            public CommunityDownloadQueueItemState State;
            public string Message;
            public string Error;
            public float Value;
            public bool HasPercent;
            public CommunityDownloadQueueDialog.DownloadQueueDialogItem Item;
        }

        private void CancelDownloadQueueItem(int? itemId = null)
        {
            if ((!itemId.HasValue || m_currentDownloadItem?.Id == itemId.Value)
                && (m_currentDownloadItem?.State == CommunityDownloadQueueItemState.Running
                || m_currentDownloadItem?.State == CommunityDownloadQueueItemState.Pausing)
                )
            {
                m_currentDownloadItem.State = CommunityDownloadQueueStateMachine.Transition(
                    m_currentDownloadItem.State,
                    CommunityDownloadQueueCommand.Cancel);
                m_currentDownloadItem.PauseRequested = false;
                m_currentDownloadItem.Progress?.Set(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadFlowManager", "CancelingDownload"), m_currentDownloadItem.FileName), 0f, false);
                m_currentDownloadItem.CancelSource?.Cancel();
                SaveQueue();
                ReportStatus(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadFlowManager", "CancelingDownload"), m_currentDownloadItem.FileName));
                return;
            }

            CommunityDownloadQueueItem item = m_downloadQueue.FindCancelable(itemId);
            if (item == null)
            {
                ReportStatus(itemId.HasValue
                    ? string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadFlowManager", "TaskCannotBeCanceled"), itemId.Value)
                    : LanguageControl.Get("ScbbsPlusDownloadFlowManager", "NoCancelableDownloadTasks"));
                return;
            }

            m_downloadQueue.CancelQueued(item, DateTime.Now);
            // 项经队列对话框取消时覆盖确认框仍开着（按钮已因终态守卫变 no-op），主动收掉。
            if (m_queuedOverwriteDialog != null && DialogsManager.Dialogs.Contains(m_queuedOverwriteDialog))
            {
                DialogsManager.HideDialog(m_queuedOverwriteDialog);
            }
            item.Error = string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadFlowManager", "DownloadCanceled"), item.FileName);
            item.Progress?.Set(item.Error, 0f, false);
            SaveQueue();
            ReportStatus(item.Error);
            StartNextQueuedDownload();
        }

        private void PauseOrResumeDownloadQueueItem(int? itemId = null)
        {
            if ((!itemId.HasValue || m_currentDownloadItem?.Id == itemId.Value)
                && m_currentDownloadItem?.State == CommunityDownloadQueueItemState.Running)
            {
                m_currentDownloadItem.State = CommunityDownloadQueueStateMachine.Transition(
                    m_currentDownloadItem.State,
                    CommunityDownloadQueueCommand.RequestPause);
                m_currentDownloadItem.PauseRequested = true;
                m_currentDownloadItem.Progress?.Set(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadFlowManager", "PausingDownload"), m_currentDownloadItem.FileName), 0f, false);
                m_currentDownloadItem.CancelSource?.Cancel();
                SaveQueue();
                ReportStatus(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadFlowManager", "PausingDownload"), m_currentDownloadItem.FileName));
                return;
            }

            CommunityDownloadQueueItem item = m_downloadQueue.FindResumable(itemId);
            if (item == null)
            {
                ReportStatus(itemId.HasValue
                    ? string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadFlowManager", "TaskCannotBePausedOrResumed"), itemId.Value)
                    : LanguageControl.Get("ScbbsPlusDownloadFlowManager", "NoDownloadTaskCanBePausedOrResumed"));
                return;
            }

            m_downloadQueue.PrepareResume(item);
            item.Progress?.Set(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadFlowManager", "ResumingDownload"), item.FileName), GetResumeProgressValue(item.TempFilePath), false);
            SaveQueue();
            ReportStatus(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadFlowManager", "ResumingDownload"), item.FileName));
            StartNextQueuedDownload();
        }

        private void RetryDownloadQueueItem(int? itemId = null)
        {
            CommunityDownloadQueueItem item = m_downloadQueue.FindRetryable(itemId);
            if (item == null)
            {
                ReportStatus(itemId.HasValue
                    ? string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadFlowManager", "TaskCannotBeRetried"), itemId.Value)
                    : LanguageControl.Get("ScbbsPlusDownloadFlowManager", "NoRetryableDownloadTasks"));
                return;
            }

            m_downloadQueue.PrepareRetry(item);
            item.Progress.Set(LanguageControl.Get("ScbbsPlusDownloadFlowManager", "Queued"), 0f, false);
            SaveQueue();
            ReportStatus(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadFlowManager", "RequeuedDownload"), item.FileName));
            StartNextQueuedDownload();
        }

        private void ClearFinishedDownloadQueueItems(int? itemId = null)
        {
            if (itemId.HasValue)
            {
                CommunityDownloadQueueItem item = m_downloadQueueItems.FirstOrDefault(queueItem => queueItem.Id == itemId.Value);
                if (item == null || !IsFinishedDownloadQueueItem(item))
                {
                    ReportStatus(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadFlowManager", "TaskCannotBeCleared"), itemId.Value));
                    return;
                }

                DeleteDownloadTempFile(item);
                item.CancelSource?.Dispose();
                item.CancelSource = null;
                // 经 coordinator 加锁移除：Items 活引用直改会与后台落盘 Save 的锁内枚举竞态。
                m_downloadQueue.Remove(item);
                SaveQueue();
                ReportStatus(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadFlowManager", "ClearedDownloadTask"), item.FileName));
                return;
            }

            foreach (CommunityDownloadQueueItem item in m_downloadQueueItems)
            {
                if (IsFinishedDownloadQueueItem(item))
                {
                    DeleteDownloadTempFile(item);
                    item.CancelSource?.Dispose();
                    item.CancelSource = null;
                }
            }
            int removed = m_downloadQueue.RemoveFinished(IsFinishedDownloadQueueItem);
            SaveQueue();
            ReportStatus(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadFlowManager", "ClearedDownloadTasks"), removed));
        }

        private static bool IsFinishedDownloadQueueItem(CommunityDownloadQueueItem item)
        {
            return item != null && CommunityDownloadQueueStateMachine.IsFinished(item.State);
        }

        private static string GetDownloadQueueItemStateText(CommunityDownloadQueueItemState state)
        {
            return state switch
            {
                CommunityDownloadQueueItemState.Pending => LanguageControl.Get("ScbbsPlusDownloadFlowManager", "Queued"),
                CommunityDownloadQueueItemState.AwaitingOverwriteConfirmation => LanguageControl.Get("ScbbsPlusDownloadFlowManager", "WaitingForOverwriteConfirmation"),
                CommunityDownloadQueueItemState.Running => LanguageControl.Get("ScbbsPlusDownloadFlowManager", "DownloadingLabel"),
                CommunityDownloadQueueItemState.Pausing => LanguageControl.Get("ScbbsPlusDownloadFlowManager", "Pausing"),
                CommunityDownloadQueueItemState.Paused => LanguageControl.Get("ScbbsPlusDownloadFlowManager", "Paused"),
                CommunityDownloadQueueItemState.Canceling => LanguageControl.Get("ScbbsPlusDownloadFlowManager", "Canceling"),
                CommunityDownloadQueueItemState.Completed => LanguageControl.Get("ScbbsPlusDownloadFlowManager", "Completed"),
                CommunityDownloadQueueItemState.Canceled => LanguageControl.Get("ScbbsPlusDownloadFlowManager", "Canceled"),
                CommunityDownloadQueueItemState.Failed => LanguageControl.Get("ScbbsPlusDownloadFlowManager", "Failed"),
                _ => string.Empty
            };
        }

        private static IProgress<DownloadProgress> CreateDownloadProgressReporter(CommunityDownloadQueueItem item)
        {
            string fileName = item.FileName;
            CommunityTransferProgress progress = item.Progress;
            bool isResuming = Storage.TryGetFileSize(item.TempFilePath, out long existingSize) && existingSize > 0;
            Progress<DownloadProgress> byteProgress = new(info =>
            {
                if (info.TotalBytes is null && info.BytesDownloaded == 0)
                {
                    progress?.Set(
                        isResuming
                            ? string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadFlowManager", "ResumingDownload"), fileName)
                            : string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadFlowManager", "Connecting"), fileName),
                        isResuming ? 0.35f : 0f,
                        !isResuming);
                    return;
                }

                if (info.TotalBytes is > 0)
                {
                    progress?.Set(
                        string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadFlowManager", "DownloadingFormat"), fileName),
                        info.BytesDownloaded / (float)info.TotalBytes.Value,
                        true);
                }
                else
                {
                    progress?.Set(
                        string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadFlowManager", "Downloading"),
                            fileName,
                            DataSizeFormatter.Format(info.BytesDownloaded)),
                        0.35f,
                        false);
                }
            });
            return byteProgress;
        }

        private static float GetResumeProgressValue(string tempFilePath)
        {
            return Storage.TryGetFileSize(tempFilePath, out long length) && length > 0 ? 0.35f : 0f;
        }

        private static void DeleteDownloadTempFile(CommunityDownloadQueueItem item)
        {
            if (!CommunityDownloadCacheFile.TryDelete(item?.TempFilePath, out string error))
            {
                Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadFlowManager", "CleanDownloadTempFileFailed"), error));
            }
        }

        private void UpdateFileDownloadDialog()
        {
            m_fileDownloadDialog?.SetProgress(m_fileDownloadProgress);
        }

        private void ShowQueuedDownloadProgressDialog(CommunityDownloadQueueItem item)
        {
            if (item?.Progress == null)
            {
                return;
            }

            if (m_fileDownloadDialog != null)
            {
                DialogsManager.HideDialog(m_fileDownloadDialog);
            }

            m_fileDownloadDialog = new CommunityDownloadDialog(
                LanguageControl.Get("ScbbsPlusDownloadFlowManager", "ImportResource"),
                () => CancelDownloadQueueItem(item.Id),
                HideQueuedDownloadProgressDialogToBackground);
            m_fileDownloadDialog.SetProgress(item.Progress);
            DialogsManager.ShowDialog(null, m_fileDownloadDialog);
        }

        private void HideQueuedDownloadProgressDialogToBackground()
        {
            if (m_currentDownloadItem != null)
            {
                ReportStatus(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadFlowManager", "BackgroundDownloading"), m_currentDownloadItem.FileName));
            }

            if (m_fileDownloadDialog == null)
            {
                return;
            }

            CommunityDownloadDialog dialog = m_fileDownloadDialog;
            m_fileDownloadDialog = null;
            DialogsManager.HideDialog(dialog);
        }

        /// <summary>
        /// 执行队列项的传输、导入和成功反馈；调用方保留 UI 进度和导入策略。
        /// 下载器与反馈上报经参数穿线（生产传组合根静态字段，测试传桩）。
        /// </summary>
        internal static async Task<CommunityFileDownloadResult> ExecuteQueuedDownloadAsync(
            CommunityHttpDownloader downloader,
            IDownloadFeedbackReporter feedbackReporter,
            CommunityDownloadQueueItem item,
            CancellationToken cancellationToken,
            IProgress<DownloadProgress> byteProgress,
            Func<System.IO.Stream, string> importAction,
            Action onImportCompleted = null)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }
            if (importAction == null)
            {
                throw new ArgumentNullException(nameof(importAction));
            }

            System.IO.MemoryStream stream;
            try
            {
                stream = await downloader.DownloadToMemoryAsync(
                    item.Url,
                    item.TempFilePath,
                    cancellationToken,
                    byteProgress);
            }
            // 与全项目契约一致：仅当队列 CT 确实请求取消时才按取消处理，
            // 网络超时抛出的 OCE（CT 未取消）由下方 OCE catch 映射为网络错误文案。
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return CreateCancellationResult(item);
            }
            catch (OperationCanceledException)
            {
                // 读空闲超时抛 CommunityReadIdleTimeoutException（OCE 子类，见 CommunityHttpDownloader），
                // 其消息对用户毫无意义且 CT 并未请求取消。将此类超时映射为清晰的网络错误文案
                //（用户取消已由上方 catch 按取消处理）。
                return new CommunityFileDownloadResult
                {
                    FileName = item.FileName,
                    DownloadFailed = true,
                    Error = LanguageControl.Get("ScbbsPlusCommon", "Text", "NetworkError")
                };
            }
            catch (Exception ex)
            {
                return new CommunityFileDownloadResult
                {
                    FileName = item.FileName,
                    DownloadFailed = true,
                    Error = ex.Message
                };
            }

            try
            {
                using (stream)
                {
                    string importedName = importAction(stream);
                    // 导入成功的瞬间即通知落盘「已导入」标记（此刻仍是后台线程）：
                    // 若等 Update 收尾才落盘，收尾前进程死亡会让恢复流程把已导入项
                    // 当 Paused 再次导入（重复导入同一世界/家具包/模组）。
                    // 吞异常：回调只做落盘，异常不得把已成功的导入翻转成失败结果。
                    try
                    {
                        onImportCompleted?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        Log.Error(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadFlowManager", "ImportCompletedCallbackFaulted"), ex.Message));
                    }
                    // 反馈是导入成功后的上报：若在此传队列 CT，用户在反馈请求在途时暂停/取消
                    // 会把已成功导入的项误标 Paused/Canceled。反馈不受队列取消影响。
                    await feedbackReporter.ReportAsync(item.FeedbackInfo, CancellationToken.None);
                    return new CommunityFileDownloadResult
                    {
                        FileName = item.FileName,
                        ImportedName = importedName,
                        Type = item.Type,
                        PostId = item.FeedbackInfo?.PostId ?? 0,
                        RequiresRestart = item.RequiresRestart
                    };
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return CreateCancellationResult(item);
            }
            catch (FileAlreadyExistsException ex)
            {
                // 引擎导入冲突以类型化异常携带既有路径，进结果供覆盖确认流程使用；
                // 不落通用 catch 嗅探错误文本。
                return new CommunityFileDownloadResult
                {
                    FileName = item.FileName,
                    ExistingFilePath = ex.Path,
                    Error = ex.Message
                };
            }
            catch (InvalidScmodArchiveException)
            {
                // 归档结构无效（G2 类型化分类）：坏包按失败计、展示统一文案；
                // 分类只看异常类型，不再嗅探消息文本。
                return new CommunityFileDownloadResult
                {
                    FileName = item.FileName,
                    DownloadFailed = item.Type == ExternalContentType.Mod,
                    Error = ScmodArchiveReader.InvalidArchiveFriendlyMessage
                };
            }
            catch (Exception ex)
            {
                return new CommunityFileDownloadResult
                {
                    FileName = item.FileName,
                    DownloadFailed = false,
                    Error = ex.Message
                };
            }
        }

        private static CommunityFileDownloadResult CreateCancellationResult(CommunityDownloadQueueItem item)
        {
            return new CommunityFileDownloadResult
            {
                FileName = item.FileName,
                Paused = item.PauseRequested,
                Canceled = !item.PauseRequested
            };
        }
    }
}