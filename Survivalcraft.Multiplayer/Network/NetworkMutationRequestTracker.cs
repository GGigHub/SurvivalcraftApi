using System.Runtime.CompilerServices;
using System.Threading;

namespace Game.Network;

public enum NetworkMutationDomain : byte {
    InventoryMove,
    InventoryDragDrop,
    InventorySelectSlot,
    PlayerDrop,
    PlayerWorldDragDrop,
    PlayerHit,
    PlayerRestart,
    PlayerInjury,
    EnvironmentTime,
    EnvironmentWeather,
    EnvironmentLightning,
    PlayerSleep,
    PlayerWakeup,
    PlayerMount,
    PlayerInteract,
    LogicEdit,
    FurnitureHammer,
    PlayerCreativeFly
}

public enum NetworkMutationSequenceDecision : byte {
    Accepted,
    Duplicate,
    Stale,
    Invalid
}

/// <summary>
/// Gives client mutations a stable identity and suppresses duplicate delivery on
/// the server without rejecting distinct requests that arrive out of order.
/// </summary>
public static class NetworkMutationRequestTracker {
    private const int MaximumRememberedRequests = 2048;
    private static long s_nextRequestId;
    private static readonly ConditionalWeakTable<ClientSession, SessionWindow> s_sessions = new();

    private sealed class SessionWindow {
        public readonly HashSet<RequestKey> Seen = [];
        public readonly Queue<RequestKey> Order = [];
        public readonly Dictionary<NetworkMutationDomain, long> Latest = [];
    }

    private readonly record struct RequestKey(NetworkMutationDomain Domain, long RequestId);

    public static long CreateRequestId() => Interlocked.Increment(ref s_nextRequestId);

    public static bool TryAccept(
        ClientSession session,
        NetworkMutationDomain domain,
        long requestId) {
        if (session == null || requestId <= 0) {
            return false;
        }
        SessionWindow window = s_sessions.GetOrCreateValue(session);
        RequestKey key = new(domain, requestId);
        lock (window) {
            if (!window.Seen.Add(key)) {
                return false;
            }
            window.Order.Enqueue(key);
            while (window.Order.Count > MaximumRememberedRequests) {
                window.Seen.Remove(window.Order.Dequeue());
            }
            return true;
        }
    }

    /// <summary>
    /// Accepts only the newest request identity for state-setting mutations.
    /// Unlike <see cref="TryAccept"/>, this prevents two distinct requests that
    /// were reordered in transit from restoring an older desired state.
    /// </summary>
    public static NetworkMutationSequenceDecision ReceiveLatest(
        ClientSession session,
        NetworkMutationDomain domain,
        long requestId) {
        if (session == null || requestId <= 0) {
            return NetworkMutationSequenceDecision.Invalid;
        }
        SessionWindow window = s_sessions.GetOrCreateValue(session);
        RequestKey key = new(domain, requestId);
        lock (window) {
            if (!window.Seen.Add(key)) {
                return NetworkMutationSequenceDecision.Duplicate;
            }
            Remember(window, key);
            if (window.Latest.TryGetValue(domain, out long latest)) {
                if (requestId < latest) {
                    return NetworkMutationSequenceDecision.Stale;
                }
                if (requestId == latest) {
                    return NetworkMutationSequenceDecision.Duplicate;
                }
            }
            window.Latest[domain] = requestId;
            return NetworkMutationSequenceDecision.Accepted;
        }
    }

    private static void Remember(SessionWindow window, RequestKey key) {
        window.Order.Enqueue(key);
        while (window.Order.Count > MaximumRememberedRequests) {
            window.Seen.Remove(window.Order.Dequeue());
        }
    }
}
