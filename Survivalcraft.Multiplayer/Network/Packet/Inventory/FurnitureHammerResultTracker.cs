namespace Game.Network;

public readonly record struct NetworkFurnitureHammerResult(
    long RequestId,
    FurnitureHammerResultCode Code,
    int DesignIndex,
    int ProducedValue,
    int ProducedCount,
    string Message);

public static class FurnitureHammerResultTracker {
    public const int MaximumRememberedResults = 256;

    private static readonly object Sync = new();
    private static readonly Dictionary<long, NetworkFurnitureHammerResult> Results = [];
    private static readonly HashSet<long> Seen = [];
    private static readonly Queue<long> Order = [];

    public static bool TryReserve(long requestId) {
        if (requestId <= 0) {
            return false;
        }
        lock (Sync) {
            if (!Seen.Add(requestId)) {
                return false;
            }
            Order.Enqueue(requestId);
            while (Order.Count > MaximumRememberedResults) {
                long expired = Order.Dequeue();
                Seen.Remove(expired);
                Results.Remove(expired);
            }
            return true;
        }
    }

    public static bool Complete(NetworkFurnitureHammerResult result) {
        if (result.RequestId <= 0) {
            return false;
        }
        lock (Sync) {
            if (!Seen.Contains(result.RequestId)) {
                return false;
            }
            Results[result.RequestId] = result;
            return true;
        }
    }

    public static bool TryTake(long requestId, out NetworkFurnitureHammerResult result) {
        lock (Sync) {
            return Results.Remove(requestId, out result);
        }
    }

    public static void Reset() {
        lock (Sync) {
            Results.Clear();
            Seen.Clear();
            Order.Clear();
        }
    }
}
