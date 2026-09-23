using System;

namespace Game
{
    public enum CommunityDownloadQueueItemState
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

    internal enum CommunityDownloadQueueCommand
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
    internal static class CommunityDownloadQueueStateMachine
    {
        public static CommunityDownloadQueueItemState NormalizeLoaded(CommunityDownloadQueueItemState state)
        {
            return state switch
            {
                CommunityDownloadQueueItemState.Running
                    or CommunityDownloadQueueItemState.Pausing
                    or CommunityDownloadQueueItemState.Canceling => CommunityDownloadQueueItemState.Paused,
                CommunityDownloadQueueItemState.AwaitingOverwriteConfirmation => CommunityDownloadQueueItemState.Pending,
                _ => state
            };
        }

        public static CommunityDownloadQueueItemState NormalizeStored(CommunityDownloadQueueItemState state)
        {
            return state switch
            {
                CommunityDownloadQueueItemState.Running
                    or CommunityDownloadQueueItemState.Pausing
                    or CommunityDownloadQueueItemState.Canceling => CommunityDownloadQueueItemState.Paused,
                _ => state
            };
        }

        public static bool IsFinished(CommunityDownloadQueueItemState state)
        {
            return state is CommunityDownloadQueueItemState.Completed
                or CommunityDownloadQueueItemState.Canceled
                or CommunityDownloadQueueItemState.Failed;
        }

        public static CommunityDownloadQueueItemState Transition(
            CommunityDownloadQueueItemState current,
            CommunityDownloadQueueCommand command)
        {
            return (current, command) switch
            {
                (CommunityDownloadQueueItemState.Pending, CommunityDownloadQueueCommand.Start)
                    => CommunityDownloadQueueItemState.Running,
                (CommunityDownloadQueueItemState.Pending, CommunityDownloadQueueCommand.RequestOverwriteConfirmation)
                    => CommunityDownloadQueueItemState.AwaitingOverwriteConfirmation,
                (CommunityDownloadQueueItemState.Running, CommunityDownloadQueueCommand.RequestOverwriteConfirmation)
                    => CommunityDownloadQueueItemState.AwaitingOverwriteConfirmation,
                (CommunityDownloadQueueItemState.Pending, CommunityDownloadQueueCommand.Cancel)
                    => CommunityDownloadQueueItemState.Canceled,
                (CommunityDownloadQueueItemState.AwaitingOverwriteConfirmation, CommunityDownloadQueueCommand.ConfirmOverwrite)
                    => CommunityDownloadQueueItemState.Pending,
                (CommunityDownloadQueueItemState.AwaitingOverwriteConfirmation, CommunityDownloadQueueCommand.Cancel)
                    => CommunityDownloadQueueItemState.Canceled,
                (CommunityDownloadQueueItemState.Running, CommunityDownloadQueueCommand.RequestPause)
                    => CommunityDownloadQueueItemState.Pausing,
                (CommunityDownloadQueueItemState.Pausing, CommunityDownloadQueueCommand.Pause)
                    => CommunityDownloadQueueItemState.Paused,
                (CommunityDownloadQueueItemState.Running, CommunityDownloadQueueCommand.Cancel)
                    => CommunityDownloadQueueItemState.Canceling,
                (CommunityDownloadQueueItemState.Pausing, CommunityDownloadQueueCommand.Cancel)
                    => CommunityDownloadQueueItemState.Canceling,
                (CommunityDownloadQueueItemState.Canceling, CommunityDownloadQueueCommand.Cancel)
                    => CommunityDownloadQueueItemState.Canceled,
                (CommunityDownloadQueueItemState.Running, CommunityDownloadQueueCommand.Complete)
                    => CommunityDownloadQueueItemState.Completed,
                (CommunityDownloadQueueItemState.Running, CommunityDownloadQueueCommand.Fail)
                    => CommunityDownloadQueueItemState.Failed,
                (CommunityDownloadQueueItemState.Paused, CommunityDownloadQueueCommand.Resume)
                    => CommunityDownloadQueueItemState.Pending,
                (CommunityDownloadQueueItemState.Paused, CommunityDownloadQueueCommand.Cancel)
                    => CommunityDownloadQueueItemState.Canceled,
                (CommunityDownloadQueueItemState.Paused, CommunityDownloadQueueCommand.Retry)
                    => CommunityDownloadQueueItemState.Pending,
                (CommunityDownloadQueueItemState.Failed, CommunityDownloadQueueCommand.Retry)
                    => CommunityDownloadQueueItemState.Pending,
                (CommunityDownloadQueueItemState.Canceled, CommunityDownloadQueueCommand.Retry)
                    => CommunityDownloadQueueItemState.Pending,
                // 用户已请求暂停/取消，但下载任务在临界点仍完成或失败时，必须落到合理终态而非抛异常卡死队列。
                // Canceling 态一律尊重用户意图落到 Canceled；Pausing 态按真实结果落到 Completed/Failed。
                (CommunityDownloadQueueItemState.Pausing, CommunityDownloadQueueCommand.Complete)
                    => CommunityDownloadQueueItemState.Completed,
                (CommunityDownloadQueueItemState.Pausing, CommunityDownloadQueueCommand.Fail)
                    => CommunityDownloadQueueItemState.Failed,
                (CommunityDownloadQueueItemState.Canceling, CommunityDownloadQueueCommand.Complete)
                    => CommunityDownloadQueueItemState.Canceled,
                (CommunityDownloadQueueItemState.Canceling, CommunityDownloadQueueCommand.Fail)
                    => CommunityDownloadQueueItemState.Canceled,
                _ => throw new InvalidOperationException(
                    $"Invalid download queue transition: {current} + {command}.")
            };
        }
    }
}
