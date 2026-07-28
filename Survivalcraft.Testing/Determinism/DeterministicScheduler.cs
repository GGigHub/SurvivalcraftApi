namespace Survivalcraft.Testing.Determinism;

public sealed class DeterministicScheduler {
    private readonly PriorityQueue<ScheduledWork, (long Due, long Sequence)> queue = new();
    private long sequence;

    public DeterministicScheduler(ManualClock? clock = null) {
        Clock = clock ?? new ManualClock();
    }

    public ManualClock Clock { get; }
    public int PendingCount => queue.Count;

    public long Schedule(TimeSpan delay, Action action) {
        ArgumentNullException.ThrowIfNull(action);
        if (delay < TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(delay));
        }

        long workSequence = sequence++;
        long due = checked(Clock.Milliseconds + (long)Math.Ceiling(delay.TotalMilliseconds));
        queue.Enqueue(new ScheduledWork(due, workSequence, action), (due, workSequence));
        return workSequence;
    }

    public int AdvanceBy(TimeSpan duration) {
        if (duration < TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }
        return AdvanceTo(checked(Clock.Milliseconds + (long)Math.Ceiling(duration.TotalMilliseconds)));
    }

    public int AdvanceTo(long targetMilliseconds) {
        if (targetMilliseconds < Clock.Milliseconds) {
            throw new InvalidOperationException(
                $"Scheduler cannot move backwards from {Clock.Milliseconds} to {targetMilliseconds}.");
        }

        int executed = 0;
        while (queue.TryPeek(out ScheduledWork? work, out (long Due, long Sequence) priority)
            && priority.Due <= targetMilliseconds) {
            queue.Dequeue();
            Clock.AdvanceTo(priority.Due);
            work.Action();
            executed++;
        }
        Clock.AdvanceTo(targetMilliseconds);
        return executed;
    }

    public int RunUntilIdle(int maximumActions = 100_000) {
        if (maximumActions <= 0) {
            throw new ArgumentOutOfRangeException(nameof(maximumActions));
        }

        int executed = 0;
        while (queue.TryPeek(out _, out (long Due, long Sequence) priority)) {
            if (executed >= maximumActions) {
                throw new InvalidOperationException(
                    $"Scheduler exceeded the {maximumActions} action safety limit.");
            }
            executed += AdvanceTo(priority.Due);
        }
        return executed;
    }

    private sealed record ScheduledWork(long Due, long Sequence, Action Action);
}
