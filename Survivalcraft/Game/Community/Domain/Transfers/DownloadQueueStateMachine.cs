using System;

namespace Game
{
    public enum DownloadQueueItemState
    {
        Pending,
        AwaitingOverwriteConfirmation,
        Running,
        Pausing,
        Paused,
        Canceling,
        Completed,
        Canceled,
        Failed
    }

    internal enum DownloadQueueCommand
    {
        Start,
        RequestOverwriteConfirmation,
        ConfirmOverwrite,
        Cancel,
        RequestPause,
        Pause,
        Resume,
        Retry,
        Complete,
        Fail
    }

    /// <summary>
    /// 下载队列的纯状态转换规则。UI、网络和文件操作不应进入这里。
    /// </summary>
    internal static class DownloadQueueStateMachine
    {
        public static DownloadQueueItemState NormalizeLoaded(DownloadQueueItemState state)
        {
            return state switch
            {
                DownloadQueueItemState.Running
                    or DownloadQueueItemState.Pausing
                    or DownloadQueueItemState.Canceling => DownloadQueueItemState.Paused,
                DownloadQueueItemState.AwaitingOverwriteConfirmation => DownloadQueueItemState.Pending,
                _ => state
            };
        }

        public static DownloadQueueItemState NormalizeStored(DownloadQueueItemState state)
        {
            return state switch
            {
                DownloadQueueItemState.Running
                    or DownloadQueueItemState.Pausing
                    or DownloadQueueItemState.Canceling => DownloadQueueItemState.Paused,
                _ => state
            };
        }

        public static bool IsFinished(DownloadQueueItemState state)
        {
            return state is DownloadQueueItemState.Completed
                or DownloadQueueItemState.Canceled
                or DownloadQueueItemState.Failed;
        }

        public static DownloadQueueItemState Transition(
            DownloadQueueItemState current,
            DownloadQueueCommand command)
        {
            return (current, command) switch
            {
                (DownloadQueueItemState.Pending, DownloadQueueCommand.Start)
                    => DownloadQueueItemState.Running,
                (DownloadQueueItemState.Pending, DownloadQueueCommand.RequestOverwriteConfirmation)
                    => DownloadQueueItemState.AwaitingOverwriteConfirmation,
                (DownloadQueueItemState.Running, DownloadQueueCommand.RequestOverwriteConfirmation)
                    => DownloadQueueItemState.AwaitingOverwriteConfirmation,
                (DownloadQueueItemState.Pending, DownloadQueueCommand.Cancel)
                    => DownloadQueueItemState.Canceled,
                (DownloadQueueItemState.AwaitingOverwriteConfirmation, DownloadQueueCommand.ConfirmOverwrite)
                    => DownloadQueueItemState.Pending,
                (DownloadQueueItemState.AwaitingOverwriteConfirmation, DownloadQueueCommand.Cancel)
                    => DownloadQueueItemState.Canceled,
                (DownloadQueueItemState.Running, DownloadQueueCommand.RequestPause)
                    => DownloadQueueItemState.Pausing,
                (DownloadQueueItemState.Pausing, DownloadQueueCommand.Pause)
                    => DownloadQueueItemState.Paused,
                (DownloadQueueItemState.Running, DownloadQueueCommand.Cancel)
                    => DownloadQueueItemState.Canceling,
                (DownloadQueueItemState.Pausing, DownloadQueueCommand.Cancel)
                    => DownloadQueueItemState.Canceling,
                (DownloadQueueItemState.Canceling, DownloadQueueCommand.Cancel)
                    => DownloadQueueItemState.Canceled,
                (DownloadQueueItemState.Running, DownloadQueueCommand.Complete)
                    => DownloadQueueItemState.Completed,
                (DownloadQueueItemState.Running, DownloadQueueCommand.Fail)
                    => DownloadQueueItemState.Failed,
                (DownloadQueueItemState.Paused, DownloadQueueCommand.Resume)
                    => DownloadQueueItemState.Pending,
                (DownloadQueueItemState.Paused, DownloadQueueCommand.Cancel)
                    => DownloadQueueItemState.Canceled,
                (DownloadQueueItemState.Paused, DownloadQueueCommand.Retry)
                    => DownloadQueueItemState.Pending,
                (DownloadQueueItemState.Failed, DownloadQueueCommand.Retry)
                    => DownloadQueueItemState.Pending,
                (DownloadQueueItemState.Canceled, DownloadQueueCommand.Retry)
                    => DownloadQueueItemState.Pending,
                // 用户已请求暂停/取消，但下载任务在临界点仍完成或失败时，必须落到合理终态而非抛异常卡死队列。
                // Canceling 态一律尊重用户意图落到 Canceled；Pausing 态按真实结果落到 Completed/Failed。
                (DownloadQueueItemState.Pausing, DownloadQueueCommand.Complete)
                    => DownloadQueueItemState.Completed,
                (DownloadQueueItemState.Pausing, DownloadQueueCommand.Fail)
                    => DownloadQueueItemState.Failed,
                (DownloadQueueItemState.Canceling, DownloadQueueCommand.Complete)
                    => DownloadQueueItemState.Canceled,
                (DownloadQueueItemState.Canceling, DownloadQueueCommand.Fail)
                    => DownloadQueueItemState.Canceled,
                _ => throw new InvalidOperationException(
                    $"Invalid download queue transition: {current} + {command}.")
            };
        }
    }
}
