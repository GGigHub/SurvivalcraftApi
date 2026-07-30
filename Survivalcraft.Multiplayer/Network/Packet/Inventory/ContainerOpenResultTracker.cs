namespace Game.Network;

public enum NetworkContainerOpenRequestKind : byte {
    ById = 1,
    ByPoint = 2
}

public enum NetworkContainerOpenResultCode : byte {
    None,
    Accepted,
    InvalidRequest,
    InventoryNotFound,
    PermissionDenied,
    UnsupportedInventory,
    ClientStateUnavailable
}

public readonly record struct NetworkContainerOpenResult(
    long RequestId,
    NetworkContainerOpenRequestKind RequestKind,
    NetworkContainerOpenResultCode Code,
    int InventoryId,
    bool Opened);

public static class ContainerOpenResultTracker {
    public const int MaximumRememberedResults = 256;

    private static readonly object Sync = new();
    private static readonly Dictionary<long, NetworkContainerOpenResult> Results = [];
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

    public static bool Complete(NetworkContainerOpenResult result) {
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

    public static bool Record(NetworkContainerOpenResult result) {
        if (!TryReserve(result.RequestId)) {
            return false;
        }
        return Complete(result);
    }

    public static bool TryTake(long requestId, out NetworkContainerOpenResult result) {
        lock (Sync) {
            if (!Results.Remove(requestId, out result)) {
                return false;
            }
            return true;
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
