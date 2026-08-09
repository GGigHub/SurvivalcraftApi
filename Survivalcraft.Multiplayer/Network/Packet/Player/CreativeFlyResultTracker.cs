namespace Game.Network;

public readonly record struct NetworkCreativeFlyResult(
    long RequestId,
    CreativeFlyResultCode Code,
    int PlayerIndex,
    int EntityId,
    bool Enabled,
    long Revision);

public static class CreativeFlyResultTracker {
    public const int MaximumRememberedResults = 256;

    private static readonly object Sync = new();
    private static readonly Dictionary<long, NetworkCreativeFlyResult> Results = [];
    private static readonly HashSet<long> Seen = [];
    private static readonly HashSet<long> Completed = [];
    private static readonly Queue<long> Order = [];
    private static long s_lastReservedRequestId;

    public static long LastReservedRequestId {
        get {
            lock (Sync) {
                return s_lastReservedRequestId;
            }
        }
    }

    public static bool TryReserve(long requestId) {
        if (requestId <= 0) {
            return false;
        }
        lock (Sync) {
            if (!Seen.Add(requestId)) {
                return false;
            }
            s_lastReservedRequestId = requestId;
            Order.Enqueue(requestId);
            while (Order.Count > MaximumRememberedResults) {
                long expired = Order.Dequeue();
                Seen.Remove(expired);
                Completed.Remove(expired);
                Results.Remove(expired);
            }
            return true;
        }
    }

    public static bool Complete(NetworkCreativeFlyResult result) {
        if (result.RequestId <= 0) {
            return false;
        }
        lock (Sync) {
            if (!Seen.Contains(result.RequestId)
                || !Completed.Add(result.RequestId)) {
                return false;
            }
            Results.Add(result.RequestId, result);
            return true;
        }
    }

    public static bool TryTake(long requestId, out NetworkCreativeFlyResult result) {
        lock (Sync) {
            return Results.Remove(requestId, out result);
        }
    }

    public static void Reset() {
        lock (Sync) {
            Results.Clear();
            Seen.Clear();
            Completed.Clear();
            Order.Clear();
            s_lastReservedRequestId = 0;
        }
    }
}
