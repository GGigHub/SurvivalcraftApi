using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Engine;

namespace Game
{
    internal sealed class DownloadQueueManager
    {
        private readonly CommunityDownloadQueuePersistence m_persistence;
        private readonly List<CommunityDownloadQueueItem> m_items = [];
        // 集合级互斥：导入完成回调会在工作流后台线程调 Save()（即时落盘「已导入」标记），
        // 与主线程的 Enqueue/RemoveAll/Load 并发。写文件本身经 JsonFileStore 原子替换，
        // 此锁只保护 m_items 集合的遍历/变更互斥。
        private readonly Lock m_gate = new();
        private int m_nextId;

        public DownloadQueueManager(CommunityDownloadQueuePersistence persistence)
        {
            m_persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        }

        public List<CommunityDownloadQueueItem> Items => m_items;
        public int NextId
        {
            get => m_nextId;
            set => m_nextId = Math.Max(0, value);
        }
        public CommunityDownloadQueueItem Current { get; private set; }

        public void SetCurrent(CommunityDownloadQueueItem item)
        {
            Current = item;
        }

        public void ClearCurrent()
        {
            Current = null;
        }

        public CommunityDownloadQueueItem Enqueue(
            string url,
            string originalUrl,
            string fileName,
            ExternalContentType type,
            bool requiresRestart,
            CommunityDownloadSourceOption source,
            CommunityDownloadFeedbackInfo feedbackInfo,
            bool replaceExistingScbbsPlusMod,
            string replaceExistingModPackageName,
            string replaceExistingModPath,
            string overwriteExistingFilePath)
        {
            lock (m_gate)
            {
                // 去重：同一 URL+文件名的未完成项（含在途/等待覆盖确认）已在队列时拒绝重复入队，
                // 双击或快速重试不再产生重复下载与二次覆盖确认。
                bool duplicate = m_items.Any(existing =>
                    existing.State is not (CommunityDownloadQueueItemState.Completed
                        or CommunityDownloadQueueItemState.Canceled
                        or CommunityDownloadQueueItemState.Failed)
                    && string.Equals(existing.Url, url, StringComparison.Ordinal)
                    && string.Equals(existing.FileName, fileName, StringComparison.Ordinal));
                if (duplicate)
                {
                    return null;
                }

                int itemId = ++m_nextId;
                CommunityDownloadQueueItem item = new()
                {
                    Id = itemId,
                    Url = url,
                    OriginalUrl = string.IsNullOrWhiteSpace(originalUrl) ? url : originalUrl,
                    FileName = fileName,
                    Type = type,
                    RequiresRestart = requiresRestart,
                    FeedbackInfo = feedbackInfo,
                    ReplaceExistingScbbsPlusMod = replaceExistingScbbsPlusMod,
                    ReplaceExistingModPackageName = replaceExistingModPackageName,
                    ReplaceExistingModPath = replaceExistingModPath,
                    OverwriteExistingFilePath = overwriteExistingFilePath,
                    Progress = new CommunityTransferProgress(),
                    State = CommunityDownloadQueueItemState.Pending,
                    Source = source,
                    TempFilePath = CommunityDownloadQueuePersistence.CreateTempFilePath(itemId, fileName),
                    CreatedTime = DateTime.Now
                };
                m_items.Add(item);
                return item;
            }
        }

        // 下列 Find*/HasAwaitingOverwriteConfirmation 读方法不加 m_gate 直接遍历 m_items：
        // 依赖约定「m_items 的变更全部发生在主线程、后台线程只经持锁 Save() 只读序列化」，
        // 主线程读不会与自身变更并发、后台 Save 不改集合，无锁读取因此安全；
        // 若未来允许后台线程变更集合，这些读方法必须改为持锁。
        public CommunityDownloadQueueItem Find(
            int? itemId,
            Func<CommunityDownloadQueueItem, bool> predicate,
            bool preferLast = false)
        {
            IEnumerable<CommunityDownloadQueueItem> items = itemId.HasValue
                ? m_items.Where(item => item.Id == itemId.Value)
                : m_items;
            return preferLast ? items.LastOrDefault(predicate) : items.FirstOrDefault(predicate);
        }

        public CommunityDownloadQueueItem FindNextPending()
        {
            return m_items.FirstOrDefault(item => item.State == CommunityDownloadQueueItemState.Pending);
        }

        public CancellationToken Begin(CommunityDownloadQueueItem item, DateTime startedTime)
        {
            if (item?.State != CommunityDownloadQueueItemState.Pending)
            {
                throw new InvalidOperationException(LanguageControl.Get("ScbbsPlusDownloadQueueDialog", "OnlyPendingDownloadsCanBeStarted"));
            }

            item.State = CommunityDownloadQueueStateMachine.Transition(item.State, CommunityDownloadQueueCommand.Start);
            item.StartedTime = startedTime;
            item.PauseRequested = false;
            item.CancelSource?.Dispose();
            item.CancelSource = new CancellationTokenSource();
            Current = item;
            return item.CancelSource.Token;
        }

        public CommunityDownloadQueueItem FindCancelable(int? itemId)
        {
            return Find(itemId, item => item.State is CommunityDownloadQueueItemState.Pending
                or CommunityDownloadQueueItemState.AwaitingOverwriteConfirmation
                or CommunityDownloadQueueItemState.Paused);
        }

        public CommunityDownloadQueueItem FindResumable(int? itemId)
        {
            return Find(itemId, item => item.State == CommunityDownloadQueueItemState.Paused, preferLast: true);
        }

        public CommunityDownloadQueueItem FindRetryable(int? itemId)
        {
            return Find(itemId, item => item.State is CommunityDownloadQueueItemState.Failed
                or CommunityDownloadQueueItemState.Canceled
                or CommunityDownloadQueueItemState.Paused, preferLast: true);
        }

        public void CancelQueued(CommunityDownloadQueueItem item, DateTime completedTime)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }
            item.State = CommunityDownloadQueueStateMachine.Transition(item.State, CommunityDownloadQueueCommand.Cancel);
            item.CompletedTime = completedTime;
        }

        public void RequestOverwriteConfirmation(CommunityDownloadQueueItem item)
        {
            if (item == null || item.State is not (CommunityDownloadQueueItemState.Pending or CommunityDownloadQueueItemState.Running))
            {
                throw new InvalidOperationException(LanguageControl.Get("ScbbsPlusDownloadQueueDialog", "OnlyPendingOrRunningCanRequestOverwrite"));
            }

            item.State = CommunityDownloadQueueStateMachine.Transition(
                item.State,
                CommunityDownloadQueueCommand.RequestOverwriteConfirmation);
        }

        public void ConfirmOverwrite(CommunityDownloadQueueItem item, string existingFilePath)
        {
            if (item?.State != CommunityDownloadQueueItemState.AwaitingOverwriteConfirmation)
            {
                throw new InvalidOperationException(LanguageControl.Get("ScbbsPlusDownloadQueueDialog", "OnlyAwaitingOverwriteCanBeConfirmed"));
            }

            item.OverwriteExistingFilePath = existingFilePath;
            item.State = CommunityDownloadQueueStateMachine.Transition(item.State, CommunityDownloadQueueCommand.ConfirmOverwrite);
        }

        public void CancelOverwrite(CommunityDownloadQueueItem item, DateTime completedTime)
        {
            if (item?.State != CommunityDownloadQueueItemState.AwaitingOverwriteConfirmation)
            {
                throw new InvalidOperationException(LanguageControl.Get("ScbbsPlusDownloadQueueDialog", "OnlyAwaitingOverwriteCanBeCanceled"));
            }

            item.State = CommunityDownloadQueueStateMachine.Transition(item.State, CommunityDownloadQueueCommand.Cancel);
            item.CompletedTime = completedTime;
        }

        public void PrepareResume(CommunityDownloadQueueItem item)
        {
            if (item?.State != CommunityDownloadQueueItemState.Paused)
            {
                throw new InvalidOperationException(LanguageControl.Get("ScbbsPlusDownloadQueueDialog", "OnlyPausedDownloadsCanBeResumed"));
            }
            item.State = CommunityDownloadQueueStateMachine.Transition(item.State, CommunityDownloadQueueCommand.Resume);
            ResetForQueue(item, resetStartedTime: false);
        }

        public void PrepareRetry(CommunityDownloadQueueItem item)
        {
            if (item == null || item.State is not (CommunityDownloadQueueItemState.Failed
                or CommunityDownloadQueueItemState.Canceled
                or CommunityDownloadQueueItemState.Paused))
            {
                throw new InvalidOperationException(LanguageControl.Get("ScbbsPlusDownloadQueueDialog", "OnlyFinishedOrPausedCanBeRetried"));
            }
            item.State = CommunityDownloadQueueStateMachine.Transition(item.State, CommunityDownloadQueueCommand.Retry);
            ResetForQueue(item, resetStartedTime: true);
            item.Progress = new CommunityTransferProgress();
        }

        public bool HasAwaitingOverwriteConfirmation()
        {
            return m_items.Any(item => item.State == CommunityDownloadQueueItemState.AwaitingOverwriteConfirmation);
        }

        public bool Load(Action<CommunityDownloadQueueItem> restoredItemAction = null)
        {
            DownloadQueuePersistedState store;
            try
            {
                store = m_persistence.Load();
            }
            catch (Exception ex)
            {
                // 队列文件整体损坏或不可读：回退为空队列，避免启动失败。
                Log.Error(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadQueueManager", "LoadDownloadQueueFailed"), ex.Message));
                m_items.Clear();
                m_nextId = 0;
                return false;
            }
            // 缺陷 3 修复前写入的旧格式盘面含明文 Token：检测到即恢复后立即重写
            // （ToStored 已剥离）。不等首次队列操作——升级后从不碰队列的用户，
            // 明文凭据会留盘到永远（审查缺陷 3 复审 F4）。
            bool hasLegacyPlaintextToken =
                store?.Items?.Any(item => !string.IsNullOrWhiteSpace(item?.FeedbackInfo?.Token)) == true;
            bool restored = Restore(store, restoredItemAction);
            if (restored && hasLegacyPlaintextToken)
            {
                Save();
            }
            return restored;
        }

        internal bool Restore(
            DownloadQueuePersistedState store,
            Action<CommunityDownloadQueueItem> restoredItemAction = null)
        {
            if (store == null)
            {
                return false;
            }

            lock (m_gate)
            {
                m_items.Clear();
                m_nextId = Math.Max(0, store.NextId);
                foreach (DownloadQueueStoredItem stored in store.Items ?? [])
                {
                    try
                    {
                        CommunityDownloadQueueItem item = m_persistence.Restore(stored);
                        if (item == null)
                        {
                            continue;
                        }
                        restoredItemAction?.Invoke(item);
                        m_items.Add(item);
                        m_nextId = Math.Max(m_nextId, item.Id);
                    }
                    catch (Exception ex)
                    {
                        // 单个坏项（反序列化或回调失败）不拖垮整条队列：跳过继续恢复其余项。
                        Log.Error(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusDownloadQueueManager", "RestoreQueueItemFailed"), ex.Message));
                    }
                }
            }
            return true;
        }

        public void Save()
        {
            lock (m_gate)
            {
                m_persistence.Save(m_items, m_nextId);
            }
        }

        public int RemoveFinished(Func<CommunityDownloadQueueItem, bool> isFinished)
        {
            lock (m_gate)
            {
                return m_items.RemoveAll(item => isFinished(item));
            }
        }

        /// <summary>移除单个项；找到并移除返回 true。主线程路径必须经此（而非 Items 活引用）变更集合，与后台落盘的 Save 互斥。</summary>
        public bool Remove(CommunityDownloadQueueItem item)
        {
            lock (m_gate)
            {
                return m_items.Remove(item);
            }
        }

        private static void ResetForQueue(CommunityDownloadQueueItem item, bool resetStartedTime)
        {
            item.Result = null;
            item.Error = null;
            item.CompletedTime = null;
            if (resetStartedTime)
            {
                item.StartedTime = null;
            }
            item.CancelSource?.Dispose();
            item.CancelSource = null;
            item.PauseRequested = false;
        }
    }
}
