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
    LogicEdit
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
}
