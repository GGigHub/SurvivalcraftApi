using System.Text.Json;
using System.Text.Json.Serialization;
using Survivalcraft.Testing.Networking;
using Survivalcraft.Testing.Processes;
using Survivalcraft.Testing.Scenarios;

namespace Survivalcraft.Testing.Soak;

public sealed record MultiplayerSoakScheduleEntry(
    int Iteration,
    string Scenario,
    int ScenarioSeed,
    string FaultClass,
    NetworkFaultProfile NetworkFaults);

public sealed record MultiplayerSoakSchedule(
    int SchemaVersion,
    int Seed,
    MultiplayerTransportKind Transport,
    bool IncludesLoss,
    MultiplayerSoakScheduleEntry[] Entries) {

    public const int CurrentSchemaVersion = 1;

    public static readonly string[] DefaultScenarioRotation = [
        TerrainInteractionMultiplayerScenario.ScenarioName,
        PlayerMotionMultiplayerScenario.ScenarioName,
        CreativeFlyMultiplayerScenario.ScenarioName,
        SurvivalStateMultiplayerScenario.ScenarioName,
        TerrainStreamingMultiplayerScenario.ScenarioName,
        EntityCreatureMultiplayerScenario.ScenarioName,
        PickableProjectileMultiplayerScenario.ScenarioName,
        InventoryHealthMultiplayerScenario.ScenarioName,
        EnvironmentMechanicsMultiplayerScenario.ScenarioName,
        MountMultiplayerScenario.ScenarioName,
        LogicMultiplayerScenario.ScenarioName,
        SessionLifecycleMultiplayerScenario.ScenarioName
    ];

    private static readonly HashSet<string> SupportedScenarios =
        new(DefaultScenarioRotation, StringComparer.Ordinal);

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public static MultiplayerSoakSchedule Generate(
        int seed,
        int iterations,
        MultiplayerTransportKind transport,
        bool includeLoss = false,
        IReadOnlyList<string>? scenarioRotation = null) {
        if (iterations is < 1 or > 10_000) {
            throw new ArgumentOutOfRangeException(
                nameof(iterations),
                "Soak iterations must be between 1 and 10000.");
        }
        if (!Enum.IsDefined(transport)) {
            throw new ArgumentOutOfRangeException(nameof(transport));
        }

        string[] scenarios = (scenarioRotation ?? DefaultScenarioRotation).ToArray();
        ValidateScenarioRotation(scenarios);
        StableRandom random = new(unchecked((ulong)(uint)seed) ^ 0xA0761D6478BD642FUL);
        MultiplayerSoakScheduleEntry[] entries = new MultiplayerSoakScheduleEntry[iterations];
        for (int iteration = 0; iteration < iterations; iteration++) {
            string scenario = scenarios[iteration % scenarios.Length];
            int latency = random.NextInt(12, 61);
            int jitter = random.NextInt(2, Math.Min(16, latency) + 1);
            int reorderDelay = random.NextInt(15, 91);
            double reorderRate = random.NextInt(25, 76) / 100d;
            bool bandwidthLimited = iteration % 3 != 0;
            int bandwidth = bandwidthLimited
                ? random.NextInt(4, 17) * 64 * 1024
                : 0;
            double generatedLossRate = random.NextInt(1, 4) / 100d;
            double lossRate = includeLoss ? generatedLossRate : 0;
            string faultClass = includeLoss
                ? bandwidthLimited ? "loss-bandwidth-reorder" : "loss-latency-reorder"
                : bandwidthLimited ? "bandwidth-reorder" : "latency-reorder";
            entries[iteration] = new MultiplayerSoakScheduleEntry(
                iteration,
                scenario,
                random.NextInt(1, int.MaxValue),
                faultClass,
                new NetworkFaultProfile(
                    Seed: random.NextInt(1, int.MaxValue),
                    BaseLatencyMilliseconds: latency,
                    JitterMilliseconds: jitter,
                    LossRate: lossRate,
                    DuplicationRate: 1,
                    ReorderRate: reorderRate,
                    ReorderDelayMilliseconds: reorderDelay,
                    BandwidthBytesPerSecond: bandwidth,
                    TargetPacketTypes: scenario switch {
                        CreativeFlyMultiplayerScenario.ScenarioName =>
                            [CreativeFlyMultiplayerScenario.FaultPacketType],
                        SurvivalStateMultiplayerScenario.ScenarioName =>
                            SurvivalStateMultiplayerScenario.CreateFaultPacketTypes(),
                        _ => null
                    }));
        }

        return new MultiplayerSoakSchedule(
            CurrentSchemaVersion,
            seed,
            transport,
            includeLoss,
            entries).Validate();
    }

    public MultiplayerSoakSchedule Validate() {
        if (SchemaVersion != CurrentSchemaVersion) {
            throw new InvalidDataException(
                $"Unsupported soak schedule schema {SchemaVersion}; expected {CurrentSchemaVersion}.");
        }
        if (!Enum.IsDefined(Transport)) {
            throw new InvalidDataException($"Unsupported soak transport '{Transport}'.");
        }
        if (Entries is not { Length: > 0 }) {
            throw new InvalidDataException("A soak schedule must contain at least one entry.");
        }
        for (int index = 0; index < Entries.Length; index++) {
            MultiplayerSoakScheduleEntry entry = Entries[index]
                ?? throw new InvalidDataException($"Soak schedule entry {index} is null.");
            if (entry.Iteration != index) {
                throw new InvalidDataException(
                    $"Soak schedule entry {index} declares iteration {entry.Iteration}.");
            }
            if (!SupportedScenarios.Contains(entry.Scenario)) {
                throw new InvalidDataException(
                    $"Soak schedule entry {index} uses unsupported scenario '{entry.Scenario}'.");
            }
            if (string.IsNullOrWhiteSpace(entry.FaultClass)) {
                throw new InvalidDataException(
                    $"Soak schedule entry {index} has no fault class.");
            }
            entry.NetworkFaults.Validate();
            if (entry.NetworkFaults.DuplicationRate <= 0
                || entry.NetworkFaults.ReorderRate <= 0) {
                throw new InvalidDataException(
                    $"Soak schedule entry {index} must exercise duplicate and reorder paths.");
            }
            if (!IncludesLoss && entry.NetworkFaults.LossRate > 0) {
                throw new InvalidDataException(
                    $"Soak schedule entry {index} injects loss while IncludesLoss is false.");
            }
        }
        if (IncludesLoss && Entries.All(entry => entry.NetworkFaults.LossRate <= 0)) {
            throw new InvalidDataException(
                "A loss-enabled soak schedule must contain at least one lossy entry.");
        }
        return this;
    }

    public async Task WriteAsync(
        string path,
        CancellationToken cancellationToken = default) {
        Validate();
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        string temporaryPath = $"{fullPath}.tmp-{Guid.NewGuid():N}";
        try {
            await using (FileStream stream = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                16 * 1024,
                useAsync: true)) {
                await JsonSerializer.SerializeAsync(
                    stream,
                    this,
                    JsonOptions,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally {
            TryDelete(temporaryPath);
        }
    }

    public static async Task<MultiplayerSoakSchedule> ReadAsync(
        string path,
        CancellationToken cancellationToken = default) {
        string fullPath = Path.GetFullPath(path);
        await using FileStream stream = File.OpenRead(fullPath);
        MultiplayerSoakSchedule schedule =
            await JsonSerializer.DeserializeAsync<MultiplayerSoakSchedule>(
                stream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException($"Soak schedule '{fullPath}' is empty or invalid.");
        return schedule.Validate();
    }

    private static void ValidateScenarioRotation(IReadOnlyList<string> scenarios) {
        if (scenarios.Count == 0) {
            throw new ArgumentException("At least one soak scenario is required.", nameof(scenarios));
        }
        for (int index = 0; index < scenarios.Count; index++) {
            if (!SupportedScenarios.Contains(scenarios[index])) {
                throw new ArgumentException(
                    $"Unsupported soak scenario '{scenarios[index]}'.",
                    nameof(scenarios));
            }
        }
    }

    private static JsonSerializerOptions CreateJsonOptions() {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web) {
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static void TryDelete(string path) {
        try {
            if (File.Exists(path)) {
                File.Delete(path);
            }
        }
        catch {
            // Best effort only; the target file is already durable.
        }
    }

    private sealed class StableRandom {
        private ulong state;

        public StableRandom(ulong seed) {
            state = seed == 0 ? 0xD1B54A32D192ED03UL : seed;
        }

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
