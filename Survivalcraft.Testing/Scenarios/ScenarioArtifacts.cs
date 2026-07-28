using System.Text.Json;
using System.Text;
using Survivalcraft.Testing.Control;
using Survivalcraft.Testing.Networking;
using Survivalcraft.Testing.Snapshots;

namespace Survivalcraft.Testing.Scenarios;

public sealed record ScenarioDefinition(
    string Name,
    int Version,
    int Seed,
    string[] Roles,
    ScenarioStepDefinition[] Steps);

public sealed record ScenarioStepDefinition(string Name, string Command);

public sealed record ScenarioStepRecord(
    string Name,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    string? Summary = null,
    string? Error = null);

public sealed record ScenarioReplay(
    string Scenario,
    int Version,
    int Seed,
    string RuntimeDirectory,
    NetworkFaultProfile NetworkFaults);

public sealed record ScenarioArtifactBundle(
    ScenarioDefinition Definition,
    ScenarioReplay Replay,
    ScenarioStepRecord[] Steps,
    IReadOnlyDictionary<string, StateSnapshot> Snapshots,
    SnapshotDiff? StateDiff,
    IReadOnlyDictionary<string, string[]> Logs,
    IReadOnlyDictionary<string, string> Metrics,
    string Outcome,
    string? Error);

public sealed record ScenarioReplayV2(
    string Scenario,
    int Version,
    int Seed,
    string RuntimeDirectory,
    string Transport,
    int WireProtocolVersion,
    int AutomationApiVersion,
    int SnapshotSchemaVersion,
    NetworkFaultProfile? NetworkFaults = null);

public sealed record ScenarioTopologyV2(
    string Transport,
    string ServerRole,
    string[] ClientRoles,
    bool RelayEnabled,
    string? RoomCode);

public sealed record ScenarioActionRecordV2(
    string Role,
    string Command,
    JsonElement Payload,
    ActionReceipt Receipt);

public sealed record ScenarioAssertionRecordV2(
    string Name,
    string Status,
    string[] Roles,
    string Detail);

public sealed record ScenarioArtifactBundleV2(
    ScenarioDefinition Definition,
    ScenarioReplayV2 Replay,
    ScenarioTopologyV2 Topology,
    ScenarioStepRecord[] Steps,
    IReadOnlyDictionary<string, AutomationDescription> Capabilities,
    ScenarioActionRecordV2[] Actions,
    IReadOnlyDictionary<string, ActionEventsResult> Events,
    IReadOnlyDictionary<string, StateSnapshotV2> Snapshots,
    ScenarioAssertionRecordV2[] Assertions,
    IReadOnlyDictionary<string, string[]> Logs,
    IReadOnlyDictionary<string, string> Metrics,
    string Outcome,
    string? Error);

public static class ScenarioArtifactWriter {
    private static readonly JsonSerializerOptions ArtifactJsonOptions =
        new(ControlProtocol.JsonOptions) {
            WriteIndented = true
        };

    public static async Task<string> WriteAsync(
        string artifactDirectory,
        ScenarioArtifactBundle bundle,
        CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactDirectory);
        ArgumentNullException.ThrowIfNull(bundle);
        string fullDirectory = Path.GetFullPath(artifactDirectory);
        Directory.CreateDirectory(fullDirectory);

        await WriteJsonAsync(
            Path.Combine(fullDirectory, "scenario.json"),
            bundle.Definition,
            cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(
            Path.Combine(fullDirectory, "replay.json"),
            bundle.Replay,
            cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(
            Path.Combine(fullDirectory, "steps.json"),
            bundle.Steps,
            cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(
            Path.Combine(fullDirectory, "metrics.json"),
            bundle.Metrics,
            cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(
            Path.Combine(fullDirectory, "outcome.json"),
            new { bundle.Outcome, bundle.Error },
            cancellationToken).ConfigureAwait(false);

        foreach ((string name, StateSnapshot snapshot) in bundle.Snapshots) {
            await WriteJsonAsync(
                Path.Combine(fullDirectory, $"snapshot-{Sanitize(name)}.json"),
                SnapshotCanonicalizer.Normalize(snapshot),
                cancellationToken).ConfigureAwait(false);
        }
        if (bundle.StateDiff != null) {
            await WriteJsonAsync(
                Path.Combine(fullDirectory, "state.diff.json"),
                bundle.StateDiff,
                cancellationToken).ConfigureAwait(false);
        }
        foreach ((string role, string[] lines) in bundle.Logs) {
            await WriteLinesAtomicallyAsync(
                Path.Combine(fullDirectory, $"{Sanitize(role)}.log"),
                lines,
                cancellationToken).ConfigureAwait(false);
        }
        return fullDirectory;
    }

    public static async Task<string> WriteV2Async(
        string artifactDirectory,
        ScenarioArtifactBundleV2 bundle,
        CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactDirectory);
        ArgumentNullException.ThrowIfNull(bundle);
        string fullDirectory = Path.GetFullPath(artifactDirectory);
        Directory.CreateDirectory(fullDirectory);

        await WriteJsonAsync(
            Path.Combine(fullDirectory, "scenario.json"),
            bundle.Definition,
            cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(
            Path.Combine(fullDirectory, "replay-v2.json"),
            bundle.Replay,
            cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(
            Path.Combine(fullDirectory, "topology.json"),
            bundle.Topology,
            cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(
            Path.Combine(fullDirectory, "steps.json"),
            bundle.Steps,
            cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(
            Path.Combine(fullDirectory, "actions.json"),
            bundle.Actions,
            cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(
            Path.Combine(fullDirectory, "assertions.json"),
            bundle.Assertions,
            cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(
            Path.Combine(fullDirectory, "metrics.json"),
            bundle.Metrics,
            cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(
            Path.Combine(fullDirectory, "outcome.json"),
            new { bundle.Outcome, bundle.Error },
            cancellationToken).ConfigureAwait(false);

        foreach ((string role, AutomationDescription description) in bundle.Capabilities) {
            await WriteJsonAsync(
                Path.Combine(fullDirectory, $"capabilities-{Sanitize(role)}.json"),
                description,
                cancellationToken).ConfigureAwait(false);
        }
        foreach ((string role, ActionEventsResult events) in bundle.Events) {
            await WriteJsonAsync(
                Path.Combine(fullDirectory, $"events-{Sanitize(role)}.json"),
                events,
                cancellationToken).ConfigureAwait(false);
        }
        foreach ((string name, StateSnapshotV2 snapshot) in bundle.Snapshots) {
            await WriteJsonAsync(
                Path.Combine(fullDirectory, $"snapshot-{Sanitize(name)}.v2.json"),
                SnapshotV2Canonicalizer.Normalize(snapshot),
                cancellationToken).ConfigureAwait(false);
        }
        foreach ((string role, string[] lines) in bundle.Logs) {
            await WriteLinesAtomicallyAsync(
                Path.Combine(fullDirectory, $"{Sanitize(role)}.log"),
                lines,
                cancellationToken).ConfigureAwait(false);
        }
        return fullDirectory;
    }

    public static async Task<ScenarioReplay> ReadReplayAsync(
        string artifactDirectory,
        CancellationToken cancellationToken = default) {
        string path = Path.Combine(Path.GetFullPath(artifactDirectory), "replay.json");
        await using FileStream stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<ScenarioReplay>(
            stream,
            ArtifactJsonOptions,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException($"Replay file '{path}' is empty or invalid.");
    }

    public static async Task<ScenarioReplayV2> ReadReplayV2Async(
        string artifactDirectory,
        CancellationToken cancellationToken = default) {
        string path = Path.Combine(Path.GetFullPath(artifactDirectory), "replay-v2.json");
        await using FileStream stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<ScenarioReplayV2>(
            stream,
            ArtifactJsonOptions,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException($"Replay file '{path}' is empty or invalid.");
    }

    public static async Task<ScenarioActionRecordV2[]> ReadActionsV2Async(
        string artifactDirectory,
        CancellationToken cancellationToken = default) {
        string path = Path.Combine(Path.GetFullPath(artifactDirectory), "actions.json");
        await using FileStream stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<ScenarioActionRecordV2[]>(
            stream,
            ArtifactJsonOptions,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException($"Action replay file '{path}' is empty or invalid.");
    }

    private static async Task WriteJsonAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken) {
        string temporaryPath = CreateTemporaryPath(path);
        try {
            await using (FileStream stream = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 16 * 1024,
                useAsync: true)) {
                await JsonSerializer.SerializeAsync(
                    stream,
                    value,
                    ArtifactJsonOptions,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally {
            TryDeleteTemporaryFile(temporaryPath);
        }
    }

    private static async Task WriteLinesAtomicallyAsync(
        string path,
        IEnumerable<string> lines,
        CancellationToken cancellationToken) {
        string temporaryPath = CreateTemporaryPath(path);
        try {
            await using (FileStream stream = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 16 * 1024,
                useAsync: true)) {
                await using StreamWriter writer = new(
                    stream,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    bufferSize: 16 * 1024,
                    leaveOpen: true);
                foreach (string line in lines) {
                    await writer.WriteLineAsync(
                        (line ?? string.Empty).AsMemory(),
                        cancellationToken).ConfigureAwait(false);
                }
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally {
            TryDeleteTemporaryFile(temporaryPath);
        }
    }

    private static string CreateTemporaryPath(string path) =>
        $"{path}.tmp-{Guid.NewGuid():N}";

    private static void TryDeleteTemporaryFile(string path) {
        try {
            if (File.Exists(path)) {
                File.Delete(path);
            }
        }
        catch {
            // A stale temporary file is preferable to hiding the scenario result.
        }
    }

    private static string Sanitize(string value) {
        foreach (char invalid in Path.GetInvalidFileNameChars()) {
            value = value.Replace(invalid, '_');
        }
        return value;
    }
}
