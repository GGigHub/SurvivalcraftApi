using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Engine;

namespace Game
{
    /// <summary>
    /// 单槽后台任务生命周期：requestId 防陈旧 + 失察任务观察。
    /// 所有方法须在 UI 线程调用（与 ScreensManager 的 Update 轮询模型配套）：
    /// Start/TryStart 启动，Update 轮询 TryTake 消费完成结果，Invalidate 作废在途结果。
    /// 被取代或丢弃的任务不会被取消，会继续运行到完成，只是结果被丢弃；
    /// 需要中断任务本身（如省流量）时由调用方用自己的 CancellationToken 处理。
    /// </summary>
    internal sealed class TaskSlot<T>
        where T : class
    {
        private readonly string m_operation;
        private Task<T> m_task;
        private int m_requestId;

        public TaskSlot(string operation)
        {
            m_operation = operation ?? LanguageControl.Get("TaskSlot", "Unspecified");
        }

        /// <summary>任务在途或结果未被 TryTake 取走均为 true；TryStart 据此忙拒绝。</summary>
        public bool IsRunning => m_task != null;
        public int RequestId => m_requestId;

        /// <summary>
        /// 启动新任务；旧任务（在途或已完成）先观察后丢弃，requestId 推进。
        /// 返回的 requestId 供调用方写入请求对象、比对结果。
        /// </summary>
        public int Start(Func<int, CancellationToken, Task<T>> load, CancellationToken token = default)
        {
            if (load == null)
            {
                throw new ArgumentNullException(nameof(load));
            }

            ObserveDiscardedTask();
            int requestId = unchecked(++m_requestId);
            m_task = Task.Run(() => load(requestId, token));
            return requestId;
        }

        /// <summary>
        /// 忙时拒绝启动：任务在途或结果未取走前不得覆盖。
        /// 失败时 requestId 为 0——合法 id 从 1 起递增，0 可作"未启动"哨兵。
        /// </summary>
        public bool TryStart(Func<int, CancellationToken, Task<T>> load, out int requestId, CancellationToken token = default)
        {
            requestId = 0;
            if (m_task != null)
            {
                return false;
            }

            requestId = Start(load, token);
            return true;
        }

        /// <summary>
        /// 取完成结果。返回 true 表示已消费（槽位清空）；result 可为 null（任务方以 null 表达
        /// "取消/无结果"，或任务 faulted 且未提供 onFaulted——此时记日志并按无结果消费，
        /// 异常不冒泡出 Update()）。调用方可经 onFaulted 把异常转换为结果对象；
        /// 仍需上抛语义时传 ex => throw 形式的转换器。
        /// 陈旧结果不可能出现：Invalidate/Start 已把旧任务移出槽位。
        /// </summary>
        public bool TryTake(out T result, Func<Exception, T> onFaulted = null)
        {
            result = null;
            Task<T> task = m_task;
            if (task == null || !task.IsCompleted)
            {
                return false;
            }

            m_task = null;
            try
            {
                result = task.GetAwaiter().GetResult();
            }
            catch (Exception ex) when (onFaulted != null)
            {
                result = onFaulted(ex);
            }
            catch (Exception ex)
            {
                Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("TaskSlot", "TaskFaultConsumedAsNoResult"), m_operation, ex.Message));
            }
            return true;
        }

        /// <summary>丢弃任务但不推进 requestId（Invalidate 语义的反面：结果仍视为当前）。</summary>
        public void Discard()
        {
            ObserveDiscardedTask();
            m_task = null;
        }

        /// <summary>作废在途/残留结果：观察旧任务、清槽并推进 requestId。</summary>
        public void Invalidate()
        {
            Discard();
            unchecked
            {
                ++m_requestId;
            }
        }

        private void ObserveDiscardedTask()
        {
            Task<T> task = m_task;
            if (task == null || task.IsCompletedSuccessfully || task.IsCanceled)
            {
                return;
            }

            _ = task.ContinueWith(completed =>
            {
                Exception error = completed.Exception;
                if (error != null)
                {
                    Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("TaskSlot", "StaleTaskFault"), m_operation, error.GetBaseException().Message));
                }
            }, TaskContinuationOptions.OnlyOnFaulted);
        }
    }
}
