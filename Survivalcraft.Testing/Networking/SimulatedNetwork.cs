using Survivalcraft.Testing.Determinism;

namespace Survivalcraft.Testing.Networking;

public sealed class SimulatedNetwork {
    private readonly Dictionary<string, Action<SimulatedPacket>> receivers =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> disconnectedEndpoints =
        new(StringComparer.Ordinal);
    private readonly DeterministicScheduler scheduler;
    private readonly StableRandom random;
    private readonly List<NetworkTraceEvent> trace = [];
    private readonly Dictionary<(string Source, string Destination), long> linkAvailableAt = [];
    private long packetSequence;

    public SimulatedNetwork(
        NetworkFaultProfile? profile = null,
        ManualClock? clock = null) {
        Profile = (profile ?? new NetworkFaultProfile()).Validate();
        scheduler = new DeterministicScheduler(clock);
        random = new StableRandom(unchecked((ulong)(uint)Profile.Seed) + 0x9E3779B97F4A7C15UL);
    }

    public NetworkFaultProfile Profile { get; }
    public ManualClock Clock => scheduler.Clock;
    public IReadOnlyList<NetworkTraceEvent> Trace => trace;
    public int PendingCount => scheduler.PendingCount;

    public void Register(string endpoint, Action<SimulatedPacket> receiver) {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        receivers.Add(endpoint, receiver ?? throw new ArgumentNullException(nameof(receiver)));
    }

    public void Disconnect(string endpoint) {
        EnsureEndpoint(endpoint);
        disconnectedEndpoints.Add(endpoint);
        trace.Add(new NetworkTraceEvent(
            Clock.Milliseconds,
            "disconnect",
            endpoint,
            endpoint,
            0,
            0,
            null));
    }

    public void Reconnect(string endpoint) {
        EnsureEndpoint(endpoint);
        disconnectedEndpoints.Remove(endpoint);
        trace.Add(new NetworkTraceEvent(
            Clock.Milliseconds,
            "reconnect",
            endpoint,
            endpoint,
            0,
            0,
            null));
    }

    public long Send(string source, string destination, ReadOnlySpan<byte> payload) {
        EnsureEndpoint(source);
        EnsureEndpoint(destination);

        long sequence = packetSequence++;
        byte[] bytes = payload.ToArray();
        if (disconnectedEndpoints.Contains(source) || disconnectedEndpoints.Contains(destination)) {
            TraceDrop("partition", source, destination, sequence, bytes.Length);
            return sequence;
        }
        if (random.NextProbability() < Profile.LossRate) {
            TraceDrop("loss", source, destination, sequence, bytes.Length);
            return sequence;
        }

        ScheduleDelivery(source, destination, sequence, bytes, duplicate: false);
        if (random.NextProbability() < Profile.DuplicationRate) {
            ScheduleDelivery(source, destination, sequence, bytes.ToArray(), duplicate: true);
            trace.Add(new NetworkTraceEvent(
                Clock.Milliseconds,
                "duplicate",
                source,
                destination,
                sequence,
                bytes.Length,
                null));
        }
        return sequence;
    }

    public int AdvanceBy(TimeSpan duration) => scheduler.AdvanceBy(duration);

    public int AdvanceTo(long targetMilliseconds) => scheduler.AdvanceTo(targetMilliseconds);

    public int RunUntilIdle(int maximumActions = 100_000) =>
        scheduler.RunUntilIdle(maximumActions);

    private void ScheduleDelivery(
        string source,
        string destination,
        long sequence,
        byte[] payload,
        bool duplicate) {
        int jitter = Profile.JitterMilliseconds == 0
            ? 0
            : random.NextInt(-Profile.JitterMilliseconds, Profile.JitterMilliseconds + 1);
        int reorderDelay = random.NextProbability() < Profile.ReorderRate
            ? Profile.ReorderDelayMilliseconds
            : 0;
        int duplicateDelay = duplicate ? 1 : 0;
        long due = checked(
            Clock.Milliseconds
            + Math.Max(0, Profile.BaseLatencyMilliseconds + jitter)
            + reorderDelay
            + duplicateDelay);

        if (Profile.BandwidthBytesPerSecond > 0) {
            (string Source, string Destination) link = (source, destination);
            long serializationMilliseconds = Math.Max(
                1,
                (long)Math.Ceiling(payload.Length * 1000d / Profile.BandwidthBytesPerSecond));
            long available = linkAvailableAt.GetValueOrDefault(link, Clock.Milliseconds);
            due = Math.Max(due, available + serializationMilliseconds);
            linkAvailableAt[link] = due;
        }

        scheduler.Schedule(
            TimeSpan.FromMilliseconds(due - Clock.Milliseconds),
            () => Deliver(source, destination, sequence, payload));
        trace.Add(new NetworkTraceEvent(
            Clock.Milliseconds,
            "scheduled",
            source,
            destination,
            sequence,
            payload.Length,
            due));
    }

    private void Deliver(
        string source,
        string destination,
        long sequence,
        byte[] payload) {
        if (disconnectedEndpoints.Contains(source) || disconnectedEndpoints.Contains(destination)) {
            TraceDrop("partition", source, destination, sequence, payload.Length);
            return;
        }
        receivers[destination](new SimulatedPacket(
            sequence,
            source,
            destination,
            Clock.Milliseconds,
            payload));
        trace.Add(new NetworkTraceEvent(
            Clock.Milliseconds,
            "delivered",
            source,
            destination,
            sequence,
            payload.Length,
            Clock.Milliseconds));
    }

    private void TraceDrop(
        string reason,
        string source,
        string destination,
        long sequence,
        int length) {
        trace.Add(new NetworkTraceEvent(
            Clock.Milliseconds,
            "dropped",
            source,
            destination,
            sequence,
            length,
            null,
            reason));
    }

    private void EnsureEndpoint(string endpoint) {
        if (!receivers.ContainsKey(endpoint)) {
            throw new InvalidOperationException(
                $"Simulated endpoint '{endpoint}' is not registered.");
        }
    }

    private sealed class StableRandom {
        private ulong state;

        public StableRandom(ulong seed) {
            state = seed == 0 ? 0xD1B54A32D192ED03UL : seed;
        }

        public double NextProbability() =>
            (NextUInt64() >> 11) * (1.0 / (1UL << 53));

        public int NextInt(int minimumInclusive, int maximumExclusive) {
            if (minimumInclusive >= maximumExclusive) {
                throw new ArgumentOutOfRangeException(nameof(maximumExclusive));
            }
            uint range = checked((uint)(maximumExclusive - minimumInclusive));
            return minimumInclusive + (int)(NextUInt64() % range);
        }

        private ulong NextUInt64() {
            ulong value = state;
            value ^= value >> 12;
            value ^= value << 25;
            value ^= value >> 27;
            state = value;
            return value * 2685821657736338717UL;
        }
    }
}

public sealed record SimulatedPacket(
    long Sequence,
    string Source,
    string Destination,
    long DeliveredAtMilliseconds,
    byte[] Payload);

public sealed record NetworkTraceEvent(
    long AtMilliseconds,
    string Kind,
    string Source,
    string Destination,
    long Sequence,
    int Bytes,
    long? DueAtMilliseconds,
    string? Reason = null);
