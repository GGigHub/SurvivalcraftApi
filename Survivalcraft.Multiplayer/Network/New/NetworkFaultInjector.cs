using Engine;
using LiteNetLib.Utils;

namespace Game.Network;

public sealed record NetworkFaultSettings(
    int Seed = 1,
    int BaseLatencyMilliseconds = 0,
    int JitterMilliseconds = 0,
    double LossRate = 0,
    double DuplicationRate = 0,
    double ReorderRate = 0,
    int ReorderDelayMilliseconds = 0,
    int BandwidthBytesPerSecond = 0,
    string[] TargetPacketTypes = null) {

    public bool Enabled =>
        BaseLatencyMilliseconds > 0
        || JitterMilliseconds > 0
        || LossRate > 0
        || DuplicationRate > 0
        || ReorderRate > 0
        || BandwidthBytesPerSecond > 0;

    public NetworkFaultSettings Validate() {
        if (BaseLatencyMilliseconds < 0
            || JitterMilliseconds < 0
            || ReorderDelayMilliseconds < 0
            || BandwidthBytesPerSecond < 0) {
            throw new ArgumentOutOfRangeException(
                nameof(BaseLatencyMilliseconds),
                "Latency, jitter, reorder delay and bandwidth cannot be negative.");
        }
        ValidateProbability(LossRate, nameof(LossRate));
        ValidateProbability(DuplicationRate, nameof(DuplicationRate));
        ValidateProbability(ReorderRate, nameof(ReorderRate));
        if (TargetPacketTypes is { Length: > 64 }) {
            throw new ArgumentOutOfRangeException(
                nameof(TargetPacketTypes),
                "At most 64 packet type filters are allowed.");
        }
        if (TargetPacketTypes != null) {
            HashSet<string> packetTypes = new(StringComparer.Ordinal);
            foreach (string packetType in TargetPacketTypes) {
                if (string.IsNullOrWhiteSpace(packetType) || packetType.Length > 256) {
                    throw new ArgumentException(
                        "Target packet types must contain 1 to 256 non-whitespace characters.",
                        nameof(TargetPacketTypes));
                }
                if (!packetTypes.Add(packetType)) {
                    throw new ArgumentException(
                        $"Target packet type '{packetType}' is duplicated.",
                        nameof(TargetPacketTypes));
                }
            }
        }
        return this;
    }

    static void ValidateProbability(double value, string parameterName) {
        if (double.IsNaN(value) || value < 0 || value > 1) {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "Probability must be between 0 and 1.");
        }
    }
}

public readonly record struct NetworkFaultMetrics(
    long Sent,
    long Dropped,
    long Duplicated,
    int Pending);

public static class NetworkFaultInjector {
    static PriorityQueue<PendingSend, (double Due, long Sequence)> m_pending = new();
    static NetworkFaultSettings m_settings = new();
    static StableRandom m_random = new(1);
    static long m_sequence;
    static long m_sent;
    static long m_dropped;
    static long m_duplicated;
    static double m_linkAvailableAt;
    static double m_orderedAvailableAt;

    public static NetworkFaultSettings Settings => m_settings;

    public static NetworkFaultMetrics Metrics =>
        new(m_sent, m_dropped, m_duplicated, m_pending.Count);

    public static void Configure(NetworkFaultSettings settings) {
        m_settings = (settings ?? throw new ArgumentNullException(nameof(settings))).Validate();
        NetworkPacketTrafficTracker.Reset();
        m_random = new StableRandom(unchecked((ulong)(uint)m_settings.Seed) + 0x9E3779B97F4A7C15UL);
        m_pending.Clear();
        m_sequence = 0;
        m_sent = 0;
        m_dropped = 0;
        m_duplicated = 0;
        m_linkAvailableAt = Time.RealTime;
        m_orderedAvailableAt = Time.RealTime;
    }

    public static void Send(
        NetDataWriter writer,
        Action<NetDataWriter> sendImmediately,
        string packetType = null) {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(sendImmediately);
        if (!m_settings.Enabled || !AppliesTo(packetType)) {
            sendImmediately(writer);
            m_sent++;
            return;
        }

        byte[] data = writer.CopyData();
        if (m_random.NextProbability() < m_settings.LossRate) {
            m_dropped++;
            return;
        }

        double originalDue = Schedule(
            data,
            sendImmediately,
            duplicate: false,
            packetType: packetType);
        if (m_random.NextProbability() < m_settings.DuplicationRate) {
            Schedule(
                data.ToArray(),
                sendImmediately,
                duplicate: true,
                minimumDue: originalDue + 0.001,
                packetType: packetType);
            m_duplicated++;
        }
    }

    public static void Update() {
        double now = Time.RealTime;
        while (m_pending.TryPeek(out PendingSend pending, out (double Due, long Sequence) priority)
            && priority.Due <= now) {
            m_pending.Dequeue();
            try {
                pending.Send(NetDataWriter.FromBytes(pending.Data, copy: false));
                m_sent++;
            }
            catch (Exception exception) {
                Log.Warning($"Network fault injector discarded a stale queued packet: {exception.Message}");
                m_dropped++;
            }
        }
    }

    public static void ClearPending() {
        m_pending.Clear();
        m_linkAvailableAt = Time.RealTime;
        m_orderedAvailableAt = Time.RealTime;
    }

    static bool AppliesTo(string packetType) =>
        m_settings.TargetPacketTypes is not { Length: > 0 }
        || (packetType != null
            && Array.IndexOf(m_settings.TargetPacketTypes, packetType) >= 0);

    static double Schedule(
        byte[] data,
        Action<NetDataWriter> sendImmediately,
        bool duplicate,
        double minimumDue = double.MinValue,
        string packetType = null) {
        int jitter = m_settings.JitterMilliseconds == 0
            ? 0
            : m_random.NextInt(
                -m_settings.JitterMilliseconds,
                m_settings.JitterMilliseconds + 1);
        bool shouldReorder = m_random.NextProbability() < m_settings.ReorderRate;
        int reorderDelay = shouldReorder
            ? m_settings.ReorderDelayMilliseconds
            : 0;
        int duplicateDelay = duplicate ? 1 : 0;
        double due = Time.RealTime
            + Math.Max(0, m_settings.BaseLatencyMilliseconds + jitter + reorderDelay + duplicateDelay) / 1000.0;
        due = Math.Max(due, minimumDue);

        if (!shouldReorder && !duplicate) {
            due = Math.Max(due, m_orderedAvailableAt);
            m_orderedAvailableAt = due;
        }

        if (m_settings.BandwidthBytesPerSecond > 0) {
            // Bandwidth is a byte-rate limit, not a packet-rate limit. A fixed
            // 1 ms floor here silently capped the link at 1000 packets/second
            // and produced an unbounded queue for many small state packets.
            double serializationSeconds =
                data.Length / (double)m_settings.BandwidthBytesPerSecond;
            due = Math.Max(due, m_linkAvailableAt + serializationSeconds);
            m_linkAvailableAt = due;
        }

        long sequence = m_sequence++;
        NetworkPacketTrafficTracker.RecordFaultTransmission(packetType, data.Length);
        m_pending.Enqueue(
            new PendingSend(data, sendImmediately),
            (due, sequence));
        return due;
    }

    readonly record struct PendingSend(
        byte[] Data,
        Action<NetDataWriter> Send);

    sealed class StableRandom {
        ulong m_state;

        public StableRandom(ulong seed) {
            m_state = seed == 0 ? 0xD1B54A32D192ED03UL : seed;
        }

        public double NextProbability() =>
            (NextUInt64() >> 11) * (1.0 / (1UL << 53));

        public int NextInt(int minimumInclusive, int maximumExclusive) {
            uint range = checked((uint)(maximumExclusive - minimumInclusive));
            return minimumInclusive + (int)(NextUInt64() % range);
        }

        ulong NextUInt64() {
            ulong value = m_state;
            value ^= value >> 12;
            value ^= value << 25;
            value ^= value >> 27;
            m_state = value;
            return value * 2685821657736338717UL;
        }
    }
}
