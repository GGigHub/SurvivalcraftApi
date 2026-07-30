namespace Game.Network;

public sealed record NetworkPacketTrafficMetrics(
    IReadOnlyDictionary<string, long> QueuedPacketsByType,
    IReadOnlyDictionary<string, long> FaultTransmissionsByType,
    IReadOnlyDictionary<string, long> FaultBytesByType);

/// <summary>
/// Bounded, process-local packet traffic evidence for diagnostics and soak artifacts.
/// Packet type cardinality is bounded by the registered protocol surface.
/// </summary>
public static class NetworkPacketTrafficTracker {
    private static readonly object Sync = new();
    private static readonly Dictionary<string, long> QueuedPackets =
        new(StringComparer.Ordinal);
    private static readonly Dictionary<string, long> FaultTransmissions =
        new(StringComparer.Ordinal);
    private static readonly Dictionary<string, long> FaultBytes =
        new(StringComparer.Ordinal);

    public static NetworkPacketTrafficMetrics Metrics {
        get {
            lock (Sync) {
                return new NetworkPacketTrafficMetrics(
                    Snapshot(QueuedPackets),
                    Snapshot(FaultTransmissions),
                    Snapshot(FaultBytes));
            }
        }
    }

    public static void RecordQueued(Packet packet) {
        ArgumentNullException.ThrowIfNull(packet);
        lock (Sync) {
            Increment(QueuedPackets, packet.GetType().Name, 1);
        }
    }

    internal static void RecordFaultTransmission(string packetType, int bytes) {
        if (string.IsNullOrWhiteSpace(packetType) || bytes < 0) {
            return;
        }
        lock (Sync) {
            Increment(FaultTransmissions, packetType, 1);
            Increment(FaultBytes, packetType, bytes);
        }
    }

    public static void Reset() {
        lock (Sync) {
            QueuedPackets.Clear();
            FaultTransmissions.Clear();
            FaultBytes.Clear();
        }
    }

    private static Dictionary<string, long> Snapshot(
        IReadOnlyDictionary<string, long> source) =>
        source
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    private static void Increment(
        IDictionary<string, long> target,
        string key,
        long amount) {
        target.TryGetValue(key, out long current);
        target[key] = checked(current + amount);
    }
}
