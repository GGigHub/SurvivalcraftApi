using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Engine;

namespace Game
{
    internal sealed class ManifestItemsLoadResult
    {
        public int RequestId { get; init; }
        public List<LocalResourceItem> Items { get; init; } = [];
        public string Error { get; init; }
    }

    /// <summary>
    /// Owns cached and deferred loading of locally stored integration-pack manifests.
    /// </summary>
    internal sealed class ModpackManifestLoadManager
    {
        private readonly Func<List<LocalResourceItem>> m_reader;
        private readonly Lock m_sync = new();
        private List<LocalResourceItem> m_cachedItems;
        private Task<ManifestItemsLoadResult> m_loadTask;
        private int m_requestId;

        public ModpackManifestLoadManager(Func<List<LocalResourceItem>> reader)
        {
            m_reader = reader ?? throw new ArgumentNullException(nameof(reader));
        }

        public bool HasCachedItems { get { lock (m_sync) return m_cachedItems != null; } }
        public bool IsLoading { get { lock (m_sync) return m_loadTask != null; } }

        public List<LocalResourceItem> GetOrLoadSynchronously()
        {
            Task discardedTask;
            int requestId;
            lock (m_sync)
            {
                if (m_cachedItems != null)
                {
                    return new List<LocalResourceItem>(m_cachedItems);
                }
                discardedTask = m_loadTask;
                m_loadTask = null;
                requestId = NextRequestId();
            }
            ObserveDiscardedTask(discardedTask);
            ManifestItemsLoadResult result = ReadSafely(requestId);
            lock (m_sync)
            {
                if (requestId == m_requestId && m_cachedItems == null)
                {
                    m_cachedItems = result.Items ?? [];
                }
                return m_cachedItems == null ? [] : new List<LocalResourceItem>(m_cachedItems);
            }
        }

        public void StartDeferredLoad()
        {
            lock (m_sync)
            {
                if (m_cachedItems != null || m_loadTask != null)
                {
                    return;
                }

                int requestId = NextRequestId();
                m_loadTask = Task.Run(() => ReadSafely(requestId));
            }
        }

        public bool TryTakeCompleted(out ManifestItemsLoadResult result)
        {
            Task<ManifestItemsLoadResult> completedTask;
            int requestId;
            lock (m_sync)
            {
                result = null;
                if (m_loadTask == null || !m_loadTask.IsCompleted)
                {
                    return false;
                }

                completedTask = m_loadTask;
                m_loadTask = null;
                requestId = m_requestId;
            }

            try
            {
                result = completedTask.Result;
            }
            catch (Exception ex)
            {
                result = new ManifestItemsLoadResult
                {
                    RequestId = requestId,
                    Error = ex.Message
                };
            }

            lock (m_sync)
            {
                if (result == null || result.RequestId != m_requestId)
                {
                    result = null;
                    return true;
                }

                m_cachedItems = result.Items ?? [];
            }
            return true;
        }

        public bool TryGetCachedItems(out List<LocalResourceItem> items)
        {
            lock (m_sync)
            {
                items = m_cachedItems == null ? null : new List<LocalResourceItem>(m_cachedItems);
                return items != null;
            }
        }

        public void Invalidate()
        {
            Task discardedTask;
            lock (m_sync)
            {
                discardedTask = m_loadTask;
                m_loadTask = null;
                m_cachedItems = null;
                NextRequestId();
            }
            ObserveDiscardedTask(discardedTask);
        }

        private ManifestItemsLoadResult ReadSafely(int requestId = 0)
        {
            try
            {
                return new ManifestItemsLoadResult
                {
                    RequestId = requestId,
                    Items = m_reader() ?? []
                };
            }
            catch (Exception ex)
            {
                Log.Warning(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ModpackManifestLoadManager", "ReadModpackListFailed"), ex.Message));
                return new ManifestItemsLoadResult
                {
                    RequestId = requestId,
                    Error = ex.Message
                };
            }
        }

        private int NextRequestId()
        {
            unchecked
            {
                return ++m_requestId;
            }
        }

        private static void ObserveDiscardedTask(Task task)
        {
            if (task == null || task.IsCompletedSuccessfully || task.IsCanceled)
            {
                return;
            }

            _ = task.ContinueWith(completed =>
            {
                Exception error = completed.Exception;
                if (error != null)
                {
                    Log.Warning(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ModpackManifestLoadManager", "StaleModpackListTaskFailed"), error.GetBaseException().Message));
                }
            }, TaskContinuationOptions.OnlyOnFaulted);
        }
    }
}
