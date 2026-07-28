namespace Survivalcraft.Testing.Determinism;

public sealed class ManualClock {
    private readonly object syncRoot = new();
    private long milliseconds;

    public ManualClock(long initialMilliseconds = 0) {
        if (initialMilliseconds < 0) {
            throw new ArgumentOutOfRangeException(nameof(initialMilliseconds));
        }
        milliseconds = initialMilliseconds;
    }

    public long Milliseconds {
        get {
            lock (syncRoot) {
                return milliseconds;
            }
        }
    }

    public TimeSpan Elapsed => TimeSpan.FromMilliseconds(Milliseconds);

    public long AdvanceBy(long deltaMilliseconds) {
        if (deltaMilliseconds < 0) {
            throw new ArgumentOutOfRangeException(nameof(deltaMilliseconds));
        }
        lock (syncRoot) {
            checked {
                milliseconds += deltaMilliseconds;
            }
            return milliseconds;
        }
    }

    public void AdvanceTo(long targetMilliseconds) {
        lock (syncRoot) {
            if (targetMilliseconds < milliseconds) {
                throw new InvalidOperationException(
                    $"Manual time cannot move backwards from {milliseconds} to {targetMilliseconds}.");
            }
            milliseconds = targetMilliseconds;
        }
    }
}
