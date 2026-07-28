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
