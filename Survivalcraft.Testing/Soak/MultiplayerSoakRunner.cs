using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Survivalcraft.Testing.Networking;
using Survivalcraft.Testing.Scenarios;
using Survivalcraft.Testing.Snapshots;

namespace Survivalcraft.Testing.Soak;

public sealed record MultiplayerSoakOptions(
    string RuntimeDirectory,
    string ArtifactDirectory,
    MultiplayerSoakSchedule Schedule,
    SoakResourceBudget ResourceBudget,
    TimeSpan? StepTimeout = null,
    TimeSpan? ScenarioTimeout = null,
    bool KeepData = false,
    bool ContinueAfterFailure = true);

public sealed record MultiplayerSoakIterationResult(
    int Iteration,
    string Scenario,
    int ScenarioSeed,
    string FaultClass,
    NetworkFaultProfile NetworkFaults,
    string ArtifactDirectory,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    long ElapsedMilliseconds,
    string Outcome,
    IReadOnlyDictionary<string, string> Metrics,
    SoakNodeFaultEvidence[] FaultEvidence,
    SoakBudgetViolation[] BudgetViolations,
    string[] EvidenceFailures,
    string? Error);

public sealed record SoakNodeFaultEvidence(
    string Role,
    int SnapshotCount,
    long MaximumSent,
    long MaximumDropped,
    long MaximumDuplicated,
    int PeakPending,
    string? TopFaultPacketType = null,
    long MaximumFaultTransmissions = 0,
    long MaximumFaultBytes = 0);

public sealed record MultiplayerSoakResourceHighWatermark(
    string Role,
    long PeakWorkingSetBytes,
    long PeakPrivateBytes,
    long MaximumWorkingSetGrowthBytes,
    int PeakHandles,
    int PeakThreads);

public sealed record MultiplayerSoakSummary(
    int SchemaVersion,
    string ArtifactDirectory,
    string ScheduleFile,
    MultiplayerSoakSchedule Schedule,
    SoakResourceBudget ResourceBudget,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    int CompletedIterations,
    string Outcome,
    MultiplayerSoakIterationResult[] Iterations,
    MultiplayerSoakResourceHighWatermark[] ResourceHighWatermarks);

public sealed class MultiplayerSoakException : Exception {
    public MultiplayerSoakException(string message, MultiplayerSoakSummary summary)
        : base(message) {
        Summary = summary;
    }

    public MultiplayerSoakSummary Summary { get; }
}

public sealed class MultiplayerSoakRunner {
    public const int CurrentSummarySchemaVersion = 1;
    public const string ScheduleFileName = "fault-schedule.json";
    public const string SummaryFileName = "soak-summary.json";

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public async Task<MultiplayerSoakSummary> RunAsync(
        MultiplayerSoakOptions options,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.RuntimeDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ArtifactDirectory);
        options.Schedule.Validate();
        options.ResourceBudget.Validate();
        ValidateOptionalTimeout(options.StepTimeout, nameof(options.StepTimeout));
        ValidateOptionalTimeout(options.ScenarioTimeout, nameof(options.ScenarioTimeout));

        string runtimeDirectory = Path.GetFullPath(options.RuntimeDirectory);
        string artifactDirectory = Path.GetFullPath(options.ArtifactDirectory);
        Directory.CreateDirectory(artifactDirectory);
        string schedulePath = Path.Combine(artifactDirectory, ScheduleFileName);
        string summaryPath = Path.Combine(artifactDirectory, SummaryFileName);
        await options.Schedule.WriteAsync(schedulePath, cancellationToken).ConfigureAwait(false);

        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        List<MultiplayerSoakIterationResult> results = [];
        await WriteSummaryAsync(
            summaryPath,
            BuildSummary(
                artifactDirectory,
                schedulePath,
                options,
                startedAt,
                DateTimeOffset.UtcNow,
                "running",
                results),
            cancellationToken).ConfigureAwait(false);

        foreach (MultiplayerSoakScheduleEntry entry in options.Schedule.Entries) {
            cancellationToken.ThrowIfCancellationRequested();
            string iterationDirectory = Path.Combine(
                artifactDirectory,
                $"iteration-{entry.Iteration:D4}-{entry.Scenario}");
            DateTimeOffset iterationStartedAt = DateTimeOffset.UtcNow;
            Stopwatch stopwatch = Stopwatch.StartNew();
            Exception? failure = null;
            try {
                await RunScenarioAsync(
                    runtimeDirectory,
                    iterationDirectory,
                    options,
                    entry,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                throw;
            }
            catch (Exception exception) {
                failure = exception;
            }
            stopwatch.Stop();

            Dictionary<string, string> metrics = await ReadMetricsAsync(
                iterationDirectory,
                cancellationToken).ConfigureAwait(false);
            SoakNodeFaultEvidence[] faultEvidence = await ReadFaultEvidenceAsync(
                iterationDirectory,
                cancellationToken).ConfigureAwait(false);
            SoakBudgetViolation[] violations =
                SoakResourceBudgetEvaluator.Evaluate(metrics, options.ResourceBudget);
            string[] evidenceFailures = ValidateFaultEvidence(
                entry,
                faultEvidence,
                options.ResourceBudget.MaximumPendingFaultPackets);
            string outcome = failure == null
                && violations.Length == 0
                && evidenceFailures.Length == 0
                ? "passed"
                : "failed";
            results.Add(new MultiplayerSoakIterationResult(
                entry.Iteration,
                entry.Scenario,
                entry.ScenarioSeed,
                entry.FaultClass,
                entry.NetworkFaults,
                iterationDirectory,
                iterationStartedAt,
                DateTimeOffset.UtcNow,
                stopwatch.ElapsedMilliseconds,
                outcome,
                metrics,
                faultEvidence,
                violations,
                evidenceFailures,
                failure?.ToString()));

            MultiplayerSoakSummary partial = BuildSummary(
                artifactDirectory,
                schedulePath,
                options,
                startedAt,
                DateTimeOffset.UtcNow,
                "running",
                results);
            await WriteSummaryAsync(summaryPath, partial, cancellationToken).ConfigureAwait(false);
            if (outcome == "failed" && !options.ContinueAfterFailure) {
                break;
            }
        }

        string finalOutcome = results.Count == options.Schedule.Entries.Length
            && results.All(result => result.Outcome == "passed")
                ? "passed"
                : "failed";
        MultiplayerSoakSummary summary = BuildSummary(
            artifactDirectory,
            schedulePath,
            options,
            startedAt,
            DateTimeOffset.UtcNow,
            finalOutcome,
            results);
        await WriteSummaryAsync(summaryPath, summary, cancellationToken).ConfigureAwait(false);
        if (finalOutcome != "passed") {
            string failedIterations = string.Join(
                ", ",
                results
                    .Where(result => result.Outcome != "passed")
                    .Select(result => result.Iteration.ToString(CultureInfo.InvariantCulture)));
            throw new MultiplayerSoakException(
                $"Multiplayer soak failed in iteration(s) [{failedIterations}]. Summary: {summaryPath}",
                summary);
        }
        return summary;
    }

    private static async Task RunScenarioAsync(
        string runtimeDirectory,
        string artifactDirectory,
        MultiplayerSoakOptions options,
        MultiplayerSoakScheduleEntry entry,
        CancellationToken cancellationToken) {
        switch (entry.Scenario) {
            case TerrainInteractionMultiplayerScenario.ScenarioName:
                _ = await new TerrainInteractionMultiplayerScenario().RunAsync(
                    new TerrainInteractionMultiplayerScenarioOptions(
                        runtimeDirectory,
                        artifactDirectory,
                        entry.ScenarioSeed,
                        options.Schedule.Transport,
                        options.StepTimeout,
                        options.ScenarioTimeout,
                        KeepData: options.KeepData,
                        NetworkFaults: entry.NetworkFaults),
                    cancellationToken).ConfigureAwait(false);
                break;
            case PlayerMotionMultiplayerScenario.ScenarioName:
                _ = await new PlayerMotionMultiplayerScenario().RunAsync(
                    new PlayerMotionMultiplayerScenarioOptions(
                        runtimeDirectory,
                        artifactDirectory,
                        entry.ScenarioSeed,
                        options.Schedule.Transport,
                        options.StepTimeout,
                        options.ScenarioTimeout,
                        KeepData: options.KeepData,
                        NetworkFaults: entry.NetworkFaults),
                    cancellationToken).ConfigureAwait(false);
                break;
            case TerrainStreamingMultiplayerScenario.ScenarioName:
                _ = await new TerrainStreamingMultiplayerScenario().RunAsync(
                    new TerrainStreamingMultiplayerScenarioOptions(
                        runtimeDirectory,
                        artifactDirectory,
                        entry.ScenarioSeed,
                        options.Schedule.Transport,
                        options.StepTimeout,
                        options.ScenarioTimeout,
                        KeepData: options.KeepData,
                        NetworkFaults: entry.NetworkFaults),
                    cancellationToken).ConfigureAwait(false);
                break;
            case EntityCreatureMultiplayerScenario.ScenarioName:
                _ = await new EntityCreatureMultiplayerScenario().RunAsync(
                    new EntityCreatureMultiplayerScenarioOptions(
                        runtimeDirectory,
                        artifactDirectory,
                        entry.ScenarioSeed,
                        options.Schedule.Transport,
                        options.StepTimeout,
                        options.ScenarioTimeout,
                        KeepData: options.KeepData,
                        NetworkFaults: entry.NetworkFaults),
                    cancellationToken).ConfigureAwait(false);
                break;
            case PickableProjectileMultiplayerScenario.ScenarioName:
                _ = await new PickableProjectileMultiplayerScenario().RunAsync(
                    new PickableProjectileMultiplayerScenarioOptions(
                        runtimeDirectory,
                        artifactDirectory,
                        entry.ScenarioSeed,
                        options.Schedule.Transport,
                        options.StepTimeout,
                        options.ScenarioTimeout,
                        KeepData: options.KeepData,
                        NetworkFaults: entry.NetworkFaults),
                    cancellationToken).ConfigureAwait(false);
                break;
            case InventoryHealthMultiplayerScenario.ScenarioName:
                _ = await new InventoryHealthMultiplayerScenario().RunAsync(
                    new InventoryHealthMultiplayerScenarioOptions(
                        runtimeDirectory,
                        artifactDirectory,
                        entry.ScenarioSeed,
                        options.Schedule.Transport,
                        options.StepTimeout,
                        options.ScenarioTimeout,
                        KeepData: options.KeepData,
                        NetworkFaults: entry.NetworkFaults),
                    cancellationToken).ConfigureAwait(false);
                break;
            case EnvironmentMechanicsMultiplayerScenario.ScenarioName:
                _ = await new EnvironmentMechanicsMultiplayerScenario().RunAsync(
                    new EnvironmentMechanicsMultiplayerScenarioOptions(
                        runtimeDirectory,
                        artifactDirectory,
                        entry.ScenarioSeed,
                        options.Schedule.Transport,
                        options.StepTimeout,
                        options.ScenarioTimeout,
                        KeepData: options.KeepData,
                        NetworkFaults: entry.NetworkFaults),
                    cancellationToken).ConfigureAwait(false);
                break;
            case MountMultiplayerScenario.ScenarioName:
                _ = await new MountMultiplayerScenario().RunAsync(
                    new MountMultiplayerScenarioOptions(
                        runtimeDirectory,
                        artifactDirectory,
                        entry.ScenarioSeed,
                        options.Schedule.Transport,
                        options.StepTimeout,
                        options.ScenarioTimeout,
                        KeepData: options.KeepData,
                        NetworkFaults: entry.NetworkFaults),
                    cancellationToken).ConfigureAwait(false);
                break;
            case LogicMultiplayerScenario.ScenarioName:
                _ = await new LogicMultiplayerScenario().RunAsync(
                    new LogicMultiplayerScenarioOptions(
                        runtimeDirectory,
                        artifactDirectory,
                        entry.ScenarioSeed,
                        options.Schedule.Transport,
                        options.StepTimeout,
                        options.ScenarioTimeout,
                        KeepData: options.KeepData,
                        NetworkFaults: entry.NetworkFaults),
                    cancellationToken).ConfigureAwait(false);
                break;
            case SessionLifecycleMultiplayerScenario.ScenarioName:
                _ = await new SessionLifecycleMultiplayerScenario().RunAsync(
                    new SessionLifecycleMultiplayerScenarioOptions(
                        runtimeDirectory,
                        artifactDirectory,
                        options.Schedule.Transport,
                        entry.ScenarioSeed,
                        options.StepTimeout,
                        options.ScenarioTimeout,
                        KeepData: options.KeepData,
                        NetworkFaults: entry.NetworkFaults),
                    cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw new InvalidDataException(
                    $"Unsupported soak scenario '{entry.Scenario}'.");
        }
    }

    private static async Task<Dictionary<string, string>> ReadMetricsAsync(
        string artifactDirectory,
        CancellationToken cancellationToken) {
        string path = Path.Combine(artifactDirectory, "metrics.json");
        if (!File.Exists(path)) {
            return [];
        }
        await using FileStream stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(
            stream,
            JsonOptions,
            cancellationToken).ConfigureAwait(false) ?? [];
    }

    private static async Task<SoakNodeFaultEvidence[]> ReadFaultEvidenceAsync(
        string artifactDirectory,
        CancellationToken cancellationToken) {
        Dictionary<string, SoakNodeFaultEvidence> evidence = new(StringComparer.Ordinal);
        foreach (string path in Directory.EnumerateFiles(
            artifactDirectory,
            "snapshot-*.v2.json",
            SearchOption.TopDirectoryOnly)) {
            cancellationToken.ThrowIfCancellationRequested();
            await using FileStream stream = File.OpenRead(path);
            StateSnapshotV2? snapshot = await JsonSerializer.DeserializeAsync<StateSnapshotV2>(
                stream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false);
            if (snapshot?.Network == null || string.IsNullOrWhiteSpace(snapshot.Node.Role)) {
                continue;
            }
            NetworkSnapshotV2 network = snapshot.Network;
            SoakNodeFaultEvidence current = evidence.GetValueOrDefault(
                snapshot.Node.Role,
                new SoakNodeFaultEvidence(snapshot.Node.Role, 0, 0, 0, 0, 0));
            string? topPacketType = current.TopFaultPacketType;
            long maximumFaultTransmissions = current.MaximumFaultTransmissions;
            long maximumFaultBytes = current.MaximumFaultBytes;
            foreach ((string packetType, long bytes) in
                     network.FaultBytesByType ?? new Dictionary<string, long>()) {
                if (bytes <= maximumFaultBytes) {
                    continue;
                }
                topPacketType = packetType;
                maximumFaultBytes = bytes;
                maximumFaultTransmissions =
                    network.FaultTransmissionsByType?.GetValueOrDefault(packetType) ?? 0;
            }
            evidence[snapshot.Node.Role] = current with {
                SnapshotCount = current.SnapshotCount + 1,
                MaximumSent = Math.Max(current.MaximumSent, network.FaultSent),
                MaximumDropped = Math.Max(current.MaximumDropped, network.FaultDropped),
                MaximumDuplicated = Math.Max(
                    current.MaximumDuplicated,
                    network.FaultDuplicated),
                PeakPending = Math.Max(current.PeakPending, network.FaultPending),
                TopFaultPacketType = topPacketType,
                MaximumFaultTransmissions = maximumFaultTransmissions,
                MaximumFaultBytes = maximumFaultBytes
            };
        }
        return evidence.Values.OrderBy(value => value.Role, StringComparer.Ordinal).ToArray();
    }

    public static string[] ValidateFaultEvidence(
        MultiplayerSoakScheduleEntry entry,
        IReadOnlyCollection<SoakNodeFaultEvidence> evidence,
        int maximumPendingFaultPackets) {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(evidence);
        if (maximumPendingFaultPackets <= 0) {
            throw new ArgumentOutOfRangeException(nameof(maximumPendingFaultPackets));
        }
        List<string> failures = [];
        if (evidence.Count == 0) {
            failures.Add("Scenario emitted no Snapshot v2 network fault evidence.");
            return failures.ToArray();
        }
        if (entry.NetworkFaults.DuplicationRate > 0
            && evidence.Sum(item => item.MaximumDuplicated) == 0) {
            failures.Add(
                "Duplicate fault profile was enabled, but no snapshot observed a duplicated packet.");
        }
        if (entry.NetworkFaults.LossRate > 0
            && evidence.Sum(item => item.MaximumDropped) == 0) {
            failures.Add(
                "Loss fault profile was enabled, but no snapshot observed a dropped packet.");
        }
        foreach (SoakNodeFaultEvidence node in evidence.Where(item =>
                     item.PeakPending > maximumPendingFaultPackets)) {
            string topTraffic = string.IsNullOrWhiteSpace(node.TopFaultPacketType)
                ? string.Empty
                : $" Top fault traffic: {node.TopFaultPacketType} "
                    + $"({node.MaximumFaultTransmissions} transmissions, "
                    + $"{node.MaximumFaultBytes} bytes).";
            failures.Add(
                $"Node '{node.Role}' queued {node.PeakPending} fault packets; "
                + $"maximum is {maximumPendingFaultPackets}."
                + topTraffic);
        }
        return failures.ToArray();
    }

    private static MultiplayerSoakSummary BuildSummary(
        string artifactDirectory,
        string schedulePath,
        MultiplayerSoakOptions options,
        DateTimeOffset startedAt,
        DateTimeOffset finishedAt,
        string outcome,
        IReadOnlyCollection<MultiplayerSoakIterationResult> results) =>
        new(
            CurrentSummarySchemaVersion,
            artifactDirectory,
            schedulePath,
            options.Schedule,
            options.ResourceBudget,
            startedAt,
            finishedAt,
            results.Count,
            outcome,
            results.ToArray(),
            BuildHighWatermarks(results));

    private static MultiplayerSoakResourceHighWatermark[] BuildHighWatermarks(
        IEnumerable<MultiplayerSoakIterationResult> results) {
        Dictionary<string, MultiplayerSoakResourceHighWatermark> highWatermarks =
            new(StringComparer.Ordinal);
        foreach (MultiplayerSoakIterationResult result in results) {
            string[] roles = result.Metrics.Keys
                .Where(key => key.StartsWith("resource.", StringComparison.Ordinal)
                    && key.EndsWith(".samples", StringComparison.Ordinal))
                .Select(key => key["resource.".Length..^".samples".Length])
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            foreach (string role in roles) {
                MultiplayerSoakResourceHighWatermark current =
                    highWatermarks.GetValueOrDefault(
                        role,
                        new MultiplayerSoakResourceHighWatermark(role, 0, 0, 0, 0, 0));
                highWatermarks[role] = current with {
                    PeakWorkingSetBytes = Math.Max(
                        current.PeakWorkingSetBytes,
                        ReadMetric(result.Metrics, role, "peakWorkingSetBytes")),
                    PeakPrivateBytes = Math.Max(
                        current.PeakPrivateBytes,
                        ReadMetric(result.Metrics, role, "peakPrivateBytes")),
                    MaximumWorkingSetGrowthBytes = Math.Max(
                        current.MaximumWorkingSetGrowthBytes,
                        ReadMetric(result.Metrics, role, "workingSetGrowthBytes")),
                    PeakHandles = Math.Max(
                        current.PeakHandles,
                        checked((int)ReadMetric(result.Metrics, role, "peakHandles"))),
                    PeakThreads = Math.Max(
                        current.PeakThreads,
                        checked((int)ReadMetric(result.Metrics, role, "peakThreads")))
                };
            }
        }
        return highWatermarks.Values.OrderBy(value => value.Role, StringComparer.Ordinal).ToArray();
    }

    private static long ReadMetric(
        IReadOnlyDictionary<string, string> metrics,
        string role,
        string name) {
        string key = $"resource.{role}.{name}";
        return metrics.TryGetValue(key, out string? text)
            && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value)
                ? value
                : 0;
    }

    private static async Task WriteSummaryAsync(
        string path,
        MultiplayerSoakSummary summary,
        CancellationToken cancellationToken) {
        string temporaryPath = $"{path}.tmp-{Guid.NewGuid():N}";
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
                    summary,
                    JsonOptions,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally {
            TryDelete(temporaryPath);
        }
    }

    private static JsonSerializerOptions CreateJsonOptions() {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web) {
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static void ValidateOptionalTimeout(TimeSpan? value, string parameterName) {
        if (value.HasValue && value.Value <= TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static void TryDelete(string path) {
        try {
            if (File.Exists(path)) {
                File.Delete(path);
            }
        }
        catch {
            // Best-effort cleanup; the durable summary takes precedence.
        }
    }
}
