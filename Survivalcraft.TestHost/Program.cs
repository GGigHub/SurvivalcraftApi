using System.Globalization;
using System.IO.Compression;
using Survivalcraft.Testing.Control;
using Survivalcraft.Testing.Networking;
using Survivalcraft.Testing.Processes;
using Survivalcraft.Testing.Scenarios;

namespace Survivalcraft.TestHost;

internal static class Program {
    private static async Task<int> Main(string[] args) {
        try {
            if (args.Length == 0 || IsHelp(args[0])) {
                PrintHelp();
                return args.Length == 0 ? 2 : 0;
            }

            string repositoryRoot = FindRepositoryRoot();
            if (args[0].Equals("doctor", StringComparison.OrdinalIgnoreCase)) {
                Dictionary<string, string?> options = ParseOptions(args, 1);
                string runtime = ResolveRuntime(repositoryRoot, options);
                await RunDoctorAsync(
                    runtime,
                    GetTimeSpan(options, "startup-timeout", TimeSpan.FromSeconds(90)),
                    GetFlag(options, "static-only")).ConfigureAwait(false);
                return 0;
            }

            if (args[0].Equals("run", StringComparison.OrdinalIgnoreCase)
                && args.Length >= 2
                && args[1].Equals(CoreMultiplayerScenario.ScenarioName, StringComparison.OrdinalIgnoreCase)) {
                Dictionary<string, string?> options = ParseOptions(args, 2);
                CoreMultiplayerScenarioOptions scenarioOptions = BuildScenarioOptions(
                    repositoryRoot,
                    options,
                    replay: null);
                CoreMultiplayerScenarioResult result = await new CoreMultiplayerScenario()
                    .RunAsync(scenarioOptions)
                    .ConfigureAwait(false);
                PrintResult(result);
                return 0;
            }

            if (args[0].Equals("run", StringComparison.OrdinalIgnoreCase)
                && args.Length >= 2
                && args[1].Equals(CompatibilityMatrixScenario.ScenarioName, StringComparison.OrdinalIgnoreCase)) {
                Dictionary<string, string?> options = ParseOptions(args, 2);
                CompatibilityMatrixScenarioOptions scenarioOptions =
                    BuildCompatibilityScenarioOptions(repositoryRoot, options, replay: null);
                CompatibilityMatrixScenarioResult result =
                    await new CompatibilityMatrixScenario()
                        .RunAsync(scenarioOptions)
                        .ConfigureAwait(false);
                PrintResult(result);
                return 0;
            }

            if (args[0].Equals("run", StringComparison.OrdinalIgnoreCase)
                && args.Length >= 2
                && args[1].Equals(RelayModChannelScenario.ScenarioName, StringComparison.OrdinalIgnoreCase)) {
                Dictionary<string, string?> options = ParseOptions(args, 2);
                RelayModChannelScenarioOptions scenarioOptions =
                    BuildRelayScenarioOptions(repositoryRoot, options, replay: null);
                RelayModChannelScenarioResult result = await new RelayModChannelScenario()
                    .RunAsync(scenarioOptions)
                    .ConfigureAwait(false);
                PrintResult(result);
                return 0;
            }

            if (args[0].Equals("replay", StringComparison.OrdinalIgnoreCase)) {
                if (args.Length < 2 || args[1].StartsWith("--", StringComparison.Ordinal)) {
                    throw new ArgumentException("replay requires the source artifact directory.");
                }
                string sourceArtifactDirectory = Path.GetFullPath(args[1]);
                Dictionary<string, string?> options = ParseOptions(args, 2);
                ScenarioReplay replay = await ScenarioArtifactWriter.ReadReplayAsync(
                    sourceArtifactDirectory).ConfigureAwait(false);
                if (replay.Scenario.Equals(
                        CoreMultiplayerScenario.ScenarioName,
                        StringComparison.Ordinal)) {
                    CoreMultiplayerScenarioOptions scenarioOptions = BuildScenarioOptions(
                        repositoryRoot,
                        options,
                        replay);
                    CoreMultiplayerScenarioResult result = await new CoreMultiplayerScenario()
                        .RunAsync(scenarioOptions)
                        .ConfigureAwait(false);
                    PrintResult(result);
                    return 0;
                }
                if (replay.Scenario.Equals(
                        CompatibilityMatrixScenario.ScenarioName,
                        StringComparison.Ordinal)) {
                    CompatibilityMatrixScenarioOptions scenarioOptions =
                        BuildCompatibilityScenarioOptions(repositoryRoot, options, replay);
                    CompatibilityMatrixScenarioResult result =
                        await new CompatibilityMatrixScenario()
                            .RunAsync(scenarioOptions)
                            .ConfigureAwait(false);
                    PrintResult(result);
                    return 0;
                }
                if (replay.Scenario.Equals(
                        RelayModChannelScenario.ScenarioName,
                        StringComparison.Ordinal)) {
                    RelayModChannelScenarioOptions scenarioOptions =
                        BuildRelayScenarioOptions(repositoryRoot, options, replay);
                    RelayModChannelScenarioResult result =
                        await new RelayModChannelScenario()
                            .RunAsync(scenarioOptions)
                            .ConfigureAwait(false);
                    PrintResult(result);
                    return 0;
                }
                throw new InvalidDataException(
                    $"Replay scenario '{replay.Scenario}' is not supported by this host.");
            }

            throw new ArgumentException(
                $"Unknown command '{string.Join(' ', args.Take(Math.Min(args.Length, 2)))}'.");
        }
        catch (CoreMultiplayerScenarioException exception) {
            Console.Error.WriteLine($"FAILED: {exception.Message}");
            Console.Error.WriteLine(exception.InnerException);
            return 1;
        }
        catch (CompatibilityMatrixScenarioException exception) {
            Console.Error.WriteLine($"FAILED: {exception.Message}");
            Console.Error.WriteLine(exception.InnerException);
            return 1;
        }
        catch (RelayModChannelScenarioException exception) {
            Console.Error.WriteLine($"FAILED: {exception.Message}");
            Console.Error.WriteLine(exception.InnerException);
            return 1;
        }
        catch (Exception exception) {
            Console.Error.WriteLine($"FAILED: {exception.Message}");
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static CoreMultiplayerScenarioOptions BuildScenarioOptions(
        string repositoryRoot,
        IReadOnlyDictionary<string, string?> options,
        ScenarioReplay? replay) {
        int seed = GetInt32(options, "seed", replay?.Seed ?? 20260728);
        string runtime = options.TryGetValue("runtime", out string? runtimeValue)
            && !string.IsNullOrWhiteSpace(runtimeValue)
                ? Path.GetFullPath(runtimeValue)
                : replay?.RuntimeDirectory ?? ResolveRuntime(repositoryRoot, options);
        string artifactDirectory = options.TryGetValue("artifacts", out string? artifactValue)
            && !string.IsNullOrWhiteSpace(artifactValue)
                ? Path.GetFullPath(artifactValue)
                : Path.Combine(
                    repositoryRoot,
                    "TestResults",
                    replay == null
                        ? $"core-multiplayer-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}"
                        : $"core-multiplayer-replay-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}");
        string? workDirectory = options.TryGetValue("work-dir", out string? workValue)
            && !string.IsNullOrWhiteSpace(workValue)
                ? Path.GetFullPath(workValue)
                : null;

        NetworkFaultProfile baseProfile = replay?.NetworkFaults
            ?? new NetworkFaultProfile(Seed: seed);
        NetworkFaultProfile profile = baseProfile with {
            Seed = seed,
            BaseLatencyMilliseconds = GetInt32(
                options,
                "latency-ms",
                replay?.NetworkFaults.BaseLatencyMilliseconds ?? 12),
            JitterMilliseconds = GetInt32(
                options,
                "jitter-ms",
                replay?.NetworkFaults.JitterMilliseconds ?? 3),
            LossRate = GetDouble(
                options,
                "loss-rate",
                replay?.NetworkFaults.LossRate ?? 0),
            DuplicationRate = GetDouble(
                options,
                "duplication-rate",
                replay?.NetworkFaults.DuplicationRate ?? 0),
            ReorderRate = GetDouble(
                options,
                "reorder-rate",
                replay?.NetworkFaults.ReorderRate ?? 0),
            ReorderDelayMilliseconds = GetInt32(
                options,
                "reorder-delay-ms",
                replay?.NetworkFaults.ReorderDelayMilliseconds ?? 0),
            BandwidthBytesPerSecond = GetInt32(
                options,
                "bandwidth-bytes",
                replay?.NetworkFaults.BandwidthBytesPerSecond ?? 0)
        };

        return new CoreMultiplayerScenarioOptions(
            runtime,
            artifactDirectory,
            seed,
            profile,
            StepTimeout: GetTimeSpan(options, "step-timeout", TimeSpan.FromSeconds(60)),
            ScenarioTimeout: GetTimeSpan(options, "scenario-timeout", TimeSpan.FromMinutes(6)),
            WorkDirectory: workDirectory,
            KeepData: GetFlag(options, "keep-data"));
    }

    private static RelayModChannelScenarioOptions BuildRelayScenarioOptions(
        string repositoryRoot,
        IReadOnlyDictionary<string, string?> options,
        ScenarioReplay? replay) {
        int seed = GetInt32(options, "seed", replay?.Seed ?? 20260728);
        string runtime = options.TryGetValue("runtime", out string? runtimeValue)
            && !string.IsNullOrWhiteSpace(runtimeValue)
                ? Path.GetFullPath(runtimeValue)
                : replay?.RuntimeDirectory ?? ResolveRuntime(repositoryRoot, options);
        string artifactDirectory = options.TryGetValue("artifacts", out string? artifactValue)
            && !string.IsNullOrWhiteSpace(artifactValue)
                ? Path.GetFullPath(artifactValue)
                : Path.Combine(
                    repositoryRoot,
                    "TestResults",
                    replay == null
                        ? $"relay-mod-channel-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}"
                        : $"relay-mod-channel-replay-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}");
        string? workDirectory = options.TryGetValue("work-dir", out string? workValue)
            && !string.IsNullOrWhiteSpace(workValue)
                ? Path.GetFullPath(workValue)
                : null;

        NetworkFaultProfile baseProfile = replay?.NetworkFaults
            ?? new NetworkFaultProfile(Seed: seed);
        NetworkFaultProfile profile = baseProfile with {
            Seed = seed,
            BaseLatencyMilliseconds = GetInt32(
                options,
                "latency-ms",
                replay?.NetworkFaults.BaseLatencyMilliseconds ?? 8),
            JitterMilliseconds = GetInt32(
                options,
                "jitter-ms",
                replay?.NetworkFaults.JitterMilliseconds ?? 2),
            LossRate = GetDouble(
                options,
                "loss-rate",
                replay?.NetworkFaults.LossRate ?? 0),
            DuplicationRate = GetDouble(
                options,
                "duplication-rate",
                replay?.NetworkFaults.DuplicationRate ?? 0),
            ReorderRate = GetDouble(
                options,
                "reorder-rate",
                replay?.NetworkFaults.ReorderRate ?? 0),
            ReorderDelayMilliseconds = GetInt32(
                options,
                "reorder-delay-ms",
                replay?.NetworkFaults.ReorderDelayMilliseconds ?? 0),
            BandwidthBytesPerSecond = GetInt32(
                options,
                "bandwidth-bytes",
                replay?.NetworkFaults.BandwidthBytesPerSecond ?? 0)
        };

        return new RelayModChannelScenarioOptions(
            runtime,
            artifactDirectory,
            seed,
            profile,
            StepTimeout: GetTimeSpan(options, "step-timeout", TimeSpan.FromSeconds(60)),
            ScenarioTimeout: GetTimeSpan(options, "scenario-timeout", TimeSpan.FromMinutes(6)),
            WorkDirectory: workDirectory,
            KeepData: GetFlag(options, "keep-data"));
    }

    private static CompatibilityMatrixScenarioOptions BuildCompatibilityScenarioOptions(
        string repositoryRoot,
        IReadOnlyDictionary<string, string?> options,
        ScenarioReplay? replay) {
        int seed = GetInt32(options, "seed", replay?.Seed ?? 20260728);
        string runtime = options.TryGetValue("runtime", out string? runtimeValue)
            && !string.IsNullOrWhiteSpace(runtimeValue)
                ? Path.GetFullPath(runtimeValue)
                : replay?.RuntimeDirectory ?? ResolveRuntime(repositoryRoot, options);
        string artifactDirectory = options.TryGetValue("artifacts", out string? artifactValue)
            && !string.IsNullOrWhiteSpace(artifactValue)
                ? Path.GetFullPath(artifactValue)
                : Path.Combine(
                    repositoryRoot,
                    "TestResults",
                    replay == null
                        ? $"compatibility-matrix-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}"
                        : $"compatibility-matrix-replay-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}");
        string? workDirectory = options.TryGetValue("work-dir", out string? workValue)
            && !string.IsNullOrWhiteSpace(workValue)
                ? Path.GetFullPath(workValue)
                : null;
        return new CompatibilityMatrixScenarioOptions(
            runtime,
            artifactDirectory,
            seed,
            StepTimeout: GetTimeSpan(options, "step-timeout", TimeSpan.FromSeconds(60)),
            ScenarioTimeout: GetTimeSpan(options, "scenario-timeout", TimeSpan.FromMinutes(5)),
            WorkDirectory: workDirectory,
            KeepData: GetFlag(options, "keep-data"));
    }

    private static async Task RunDoctorAsync(
        string runtimeDirectory,
        TimeSpan startupTimeout,
        bool staticOnly) {
        Console.WriteLine($"runtime={runtimeDirectory}");
        string[] requiredFiles = [
            "Survivalcraft.exe",
            "Survivalcraft.dll",
            "Survivalcraft.Networking.dll",
            "Content.zip",
            Path.Combine("Mods", "Survivalcraft.TestAutomation.scmod")
        ];
        foreach (string relativePath in requiredFiles) {
            string path = Path.Combine(runtimeDirectory, relativePath);
            if (!File.Exists(path)) {
                throw new FileNotFoundException($"Required runtime file is missing: {path}", path);
            }
            Console.WriteLine($"ok file {relativePath}");
        }

        string modPath = Path.Combine(
            runtimeDirectory,
            "Mods",
            "Survivalcraft.TestAutomation.scmod");
        using (ZipArchive archive = ZipFile.OpenRead(modPath)) {
            string[] requiredEntries = [
                "modinfo.json",
                "Survivalcraft.TestAutomation.dll",
                "Survivalcraft.Testing.dll"
            ];
            foreach (string entry in requiredEntries) {
                if (!archive.Entries.Any(candidate =>
                    candidate.FullName.Equals(entry, StringComparison.OrdinalIgnoreCase))) {
                    throw new InvalidDataException(
                        $"Automation mod '{modPath}' does not contain '{entry}'.");
                }
                Console.WriteLine($"ok mod-entry {entry}");
            }
        }

        Console.WriteLine($"ok protocol v{ControlProtocol.CurrentVersion}");
        if (staticOnly) {
            Console.WriteLine("DOCTOR PASSED (static only)");
            return;
        }

        string doctorRoot = Path.Combine(
            Path.GetTempPath(),
            "Survivalcraft.TestHost",
            $"doctor-{Guid.NewGuid():N}");
        GameNodeProcess? node = null;
        try {
            node = new GameNodeProcess(new GameNodeOptions(
                "doctor",
                runtimeDirectory,
                Path.Combine(doctorRoot, "data"),
                StartupTimeout: startupTimeout));
            await node.StartAsync().ConfigureAwait(false);
            NodeStatus status = await node.WaitForStatusAsync(
                value => value.ContentLoaded && value.Screen == "MainMenuScreen",
                startupTimeout,
                "Doctor node did not reach MainMenu.").ConfigureAwait(false);
            Console.WriteLine(
                $"ok headless-control pid={node.ProcessId} screen={status.Screen} role={status.Role}");
            Console.WriteLine("DOCTOR PASSED");
        }
        catch {
            if (node != null) {
                Console.Error.WriteLine(node.GetRecentLogText());
            }
            throw;
        }
        finally {
            if (node != null) {
                await node.DisposeAsync().ConfigureAwait(false);
            }
            TryDeleteDoctorDirectory(doctorRoot);
        }
    }

    private static void TryDeleteDoctorDirectory(string doctorRoot) {
        try {
            string fullPath = Path.GetFullPath(doctorRoot);
            string allowedRoot = Path.GetFullPath(
                Path.Combine(Path.GetTempPath(), "Survivalcraft.TestHost"));
            if (fullPath.StartsWith(
                    allowedRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase)
                && Directory.Exists(fullPath)) {
                Directory.Delete(fullPath, recursive: true);
            }
        }
        catch {
            // A leftover doctor directory is preferable to hiding the actual result.
        }
    }

    private static void PrintResult(CoreMultiplayerScenarioResult result) {
        Console.WriteLine("SCENARIO PASSED");
        Console.WriteLine($"artifacts={result.ArtifactDirectory}");
        Console.WriteLine($"world={result.WorldDirectoryName}");
        Console.WriteLine(
            $"cell=({result.VerifiedCell.X},{result.VerifiedCell.Y},{result.VerifiedCell.Z}) "
            + $"value={result.VerifiedCell.Value}");
        Console.WriteLine($"elapsed={result.Elapsed}");
    }

    private static void PrintResult(CompatibilityMatrixScenarioResult result) {
        Console.WriteLine("SCENARIO PASSED");
        Console.WriteLine($"artifacts={result.ArtifactDirectory}");
        foreach ((string name, string code) in result.RejectionCodes) {
            Console.WriteLine($"rejection.{name}={code}");
        }
        Console.WriteLine($"elapsed={result.Elapsed}");
    }

    private static void PrintResult(RelayModChannelScenarioResult result) {
        Console.WriteLine("SCENARIO PASSED");
        Console.WriteLine($"artifacts={result.ArtifactDirectory}");
        Console.WriteLine($"room={result.RoomCode}");
        Console.WriteLine($"deduplicatedFrames={result.DeduplicatedFrames}");
        Console.WriteLine($"elapsed={result.Elapsed}");
    }

    private static string ResolveRuntime(
        string repositoryRoot,
        IReadOnlyDictionary<string, string?> options) {
        if (options.TryGetValue("runtime", out string? value)
            && !string.IsNullOrWhiteSpace(value)) {
            return Path.GetFullPath(value);
        }
        string configuration = options.TryGetValue("configuration", out string? configurationValue)
            && !string.IsNullOrWhiteSpace(configurationValue)
                ? configurationValue
                : "Debug";
        return Path.Combine(
            repositoryRoot,
            "Survivalcraft.Test",
            "bin",
            configuration,
            "net10.0-windows");
    }

    private static Dictionary<string, string?> ParseOptions(string[] args, int startIndex) {
        Dictionary<string, string?> result = new(StringComparer.OrdinalIgnoreCase);
        for (int index = startIndex; index < args.Length; index++) {
            string token = args[index];
            if (!token.StartsWith("--", StringComparison.Ordinal) || token.Length == 2) {
                throw new ArgumentException($"Unexpected positional argument '{token}'.");
            }
            string key = token[2..];
            string? value = null;
            if (index + 1 < args.Length
                && !args[index + 1].StartsWith("--", StringComparison.Ordinal)) {
                value = args[++index];
            }
            if (!result.TryAdd(key, value)) {
                throw new ArgumentException($"Option '--{key}' was specified more than once.");
            }
        }
        return result;
    }

    private static bool GetFlag(IReadOnlyDictionary<string, string?> options, string name) {
        if (!options.TryGetValue(name, out string? value)) {
            return false;
        }
        if (value == null) {
            return true;
        }
        if (bool.TryParse(value, out bool parsed)) {
            return parsed;
        }
        throw new ArgumentException($"Option '--{name}' expects true or false.");
    }

    private static int GetInt32(
        IReadOnlyDictionary<string, string?> options,
        string name,
        int defaultValue) {
        if (!options.TryGetValue(name, out string? value)) {
            return defaultValue;
        }
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)) {
            return parsed;
        }
        throw new ArgumentException($"Option '--{name}' expects an integer.");
    }

    private static double GetDouble(
        IReadOnlyDictionary<string, string?> options,
        string name,
        double defaultValue) {
        if (!options.TryGetValue(name, out string? value)) {
            return defaultValue;
        }
        if (double.TryParse(
            value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out double parsed)) {
            return parsed;
        }
        throw new ArgumentException($"Option '--{name}' expects a number.");
    }

    private static TimeSpan GetTimeSpan(
        IReadOnlyDictionary<string, string?> options,
        string name,
        TimeSpan defaultValue) {
        double seconds = GetDouble(options, name, defaultValue.TotalSeconds);
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0) {
            throw new ArgumentOutOfRangeException(name, "Timeout must be a positive number of seconds.");
        }
        return TimeSpan.FromSeconds(seconds);
    }

    private static string FindRepositoryRoot() {
        string[] seeds = [Environment.CurrentDirectory, AppContext.BaseDirectory];
        foreach (string seed in seeds) {
            DirectoryInfo? directory = new(Path.GetFullPath(seed));
            while (directory != null) {
                if (File.Exists(Path.Combine(directory.FullName, "SurvivalcraftApi.sln"))) {
                    return directory.FullName;
                }
                directory = directory.Parent;
            }
        }
        throw new DirectoryNotFoundException(
            "Could not find SurvivalcraftApi.sln from the current or application directory.");
    }

    private static bool IsHelp(string value) =>
        value is "-h" or "--help" or "help";

    private static void PrintHelp() {
        Console.WriteLine("Survivalcraft multiplayer automation host");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  doctor [--runtime PATH] [--static-only]");
        Console.WriteLine("  run core-multiplayer [options]");
        Console.WriteLine("  run compatibility-matrix [options]");
        Console.WriteLine("  run relay-mod-channel [options]");
        Console.WriteLine("  replay ARTIFACT_DIRECTORY [options]");
        Console.WriteLine();
        Console.WriteLine("Common options:");
        Console.WriteLine("  --runtime PATH             Built Survivalcraft.Test runtime");
        Console.WriteLine("  --artifacts PATH           Durable result bundle directory");
        Console.WriteLine("  --seed INTEGER             Deterministic scenario seed");
        Console.WriteLine("  --step-timeout SECONDS     Per-step deadline (default 60)");
        Console.WriteLine("  --scenario-timeout SECONDS Overall deadline (default 360)");
        Console.WriteLine("  --latency-ms INTEGER       Injected application-packet latency");
        Console.WriteLine("  --jitter-ms INTEGER        Injected deterministic jitter");
        Console.WriteLine("  --loss-rate NUMBER         Packet loss probability [0,1]");
        Console.WriteLine("  --keep-data                Keep isolated game data directories");
    }
}
