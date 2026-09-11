using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace Game
{
    /// <summary>
    /// 弱引用事件源：静态单例事件强持订阅者引用，换语言整表
    /// 重建屏幕后旧屏实例不获任何生命周期回调、自救退订永不触发，被静态委托钉到
    /// 进程退出。本源只持订阅目标的弱引用，目标回收后条目自动剔除。
    /// 静态方法订阅（handler.Target == null）无泄漏面，走强引用。
    /// 同一 (目标, 方法) 重复订阅只登记一次（+= 语义收敛为幂等，配合配对退订）。
    /// </summary>
    internal sealed class WeakEventSource<T>
    {
        private sealed class Entry
        {
            // null = 静态方法订阅（无目标可弱化），此时 StrongHandler 有效。
            public WeakReference TargetRef;
            public MethodInfo Method;
            public Action<T> StrongHandler;
        }

        private readonly Lock m_sync = new();
        private readonly List<Entry> m_entries = [];

        public int AliveEntryCountForTests
        {
            get
            {
                lock (m_sync)
                {
                    return m_entries.Count(entry => entry.TargetRef == null || entry.TargetRef.IsAlive);
                }
            }
        }

        public void Subscribe(Action<T> handler)
        {
            if (handler == null)
            {
                return;
            }
            lock (m_sync)
            {
                // 多播委托按调用列表逐项登记：只存 handler.Target/Method 会漏掉其余组合项。
                foreach (object invocation in handler.GetInvocationList())
                {
                    Action<T> single = (Action<T>)invocation;
                    if (m_entries.Any(entry => SameHandler(entry, single)))
                    {
                        continue;
                    }
                    m_entries.Add(single.Target == null
                        ? new Entry { TargetRef = null, StrongHandler = single }
                        : new Entry { TargetRef = new WeakReference(single.Target), Method = single.Method });
                }
            }
        }

        public void Unsubscribe(Action<T> handler)
        {
            if (handler == null)
            {
                return;
            }
            lock (m_sync)
            {
                // 多播委托按调用列表逐项移除，与 Subscribe 的逐项登记对称：
                // 只比对 handler.Target/Method 等于只比对了多播末项，会漏掉其余组合项。
                foreach (object invocation in handler.GetInvocationList())
                {
                    Action<T> single = (Action<T>)invocation;
                    m_entries.RemoveAll(entry => SameHandler(entry, single));
                }
            }
        }

        public void Raise(T args)
        {
            Entry[] snapshot;
            lock (m_sync)
            {
                // 目标已回收的条目就地剔除，列表不随历史订阅无限增长。
                m_entries.RemoveAll(entry => entry.TargetRef != null && !entry.TargetRef.IsAlive);
                snapshot = [.. m_entries];
            }
            foreach (Entry entry in snapshot)
            {
                if (entry.TargetRef == null)
                {
                    entry.StrongHandler(args);
                    continue;
                }
                if (entry.TargetRef.Target is not { } target)
                {
                    continue;
                }
                try
                {
                    entry.Method.Invoke(target, [args]);
                }
                catch (TargetInvocationException ex)
                {
                    // Invoke 包装的异常还原原始异常传播，保持直调事件语义。
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException ?? ex).Throw();
                }
            }
        }

        private static bool SameHandler(Entry entry, Action<T> handler)
        {
            if (entry.TargetRef == null)
            {
                return handler.Target == null && entry.StrongHandler.Equals(handler);
            }
            return ReferenceEquals(entry.TargetRef.Target, handler.Target) && entry.Method.Equals(handler.Method);
        }
    }
}
