using System;
using System.Threading.Tasks;

namespace Survivalcraft.Test;

public static class TestTimeouts {
    // 轮询间隔
    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);
    // AssertRemains方法用，一般用来检查变量不变(在ShortObservation指定的时间内)
    public static readonly TimeSpan ShortObservation = TimeSpan.FromMilliseconds(500);
    // UntilAsync方法用，一般用作超时，每PollInterval轮询一次(超时内)
    public static readonly TimeSpan DefaultEventually = TimeSpan.FromMilliseconds(5000);
    // WaitLog方法用，超时更长(因为有时要等更长，比如说等待进入PlayerScreen)
    public static readonly TimeSpan LongEventually = TimeSpan.FromMilliseconds(15000);
}

public static class TestWaiter {
    public static async Task<WaitResult<T>> TryUntilAsync<T>(
        Func<Task<T>> probe,
        Func<T, bool> predicate,
        TimeSpan timeout,
        TimeSpan? pollInterval = null) {
        DateTime deadline = DateTime.UtcNow + timeout;
        TimeSpan delay = pollInterval ?? TestTimeouts.PollInterval;
        T last = await probe();

        while (DateTime.UtcNow < deadline) {
            if (predicate(last)) {
                return new WaitResult<T>(true, last);
            }

            await Task.Delay(delay);
            last = await probe();
        }

        return new WaitResult<T>(predicate(last), last);
    }

    public static async Task<T> UntilAsync<T>(
        Func<Task<T>> probe,
        Func<T, bool> predicate,
        TimeSpan timeout,
        string failureMessage,
        TimeSpan? pollInterval = null) {
        WaitResult<T> result = await TryUntilAsync(probe, predicate, timeout, pollInterval);
        if (result.Matched) {
            return result.Last;
        }

        throw new TimeoutException($"{failureMessage} Last={result.Last}.");
    }

    public static async Task AssertRemainsAsync<T>(
        Func<Task<T>> probe,
        Func<T, bool> predicate,
        TimeSpan duration,
        string failureMessage,
        TimeSpan? pollInterval = null) {
        DateTime deadline = DateTime.UtcNow + duration;
        TimeSpan delay = pollInterval ?? TestTimeouts.PollInterval;
        T last = await probe();

        while (DateTime.UtcNow < deadline) {
            if (!predicate(last)) {
                throw new TimeoutException($"{failureMessage} Last={last}.");
            }

            await Task.Delay(delay);
            last = await probe();
        }

        if (!predicate(last)) {
            throw new TimeoutException($"{failureMessage} Last={last}.");
        }
    }
}

public readonly record struct WaitResult<T>(bool Matched, T Last);
