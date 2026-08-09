using System.Net;
using Engine;
using Game;
using Game.Network;
using GameEntitySystem;
using Survivalcraft.Testing.Control;
using Survivalcraft.Testing.Networking;
using Survivalcraft.Testing.Snapshots;

namespace Survivalcraft.TestAutomation;

public partial class TestAutomationModLoader {
    private NamedPipeControlServer? typedControlServer;
    private TypedCommandRouter? typedCommandRouter;
    private readonly ActionEventJournal actionEventJournal = new();
    private long typedResponseSequence;
    private string nodeRole = "unknown";

    private void StartTypedControlServer() {
        string[] args = Environment.GetCommandLineArgs();
        string? pipeName = GetArgument(args, "--control-pipe");
        nodeRole = GetArgument(args, "--role") ?? "unknown";
        if (!HasSwitch(args, "--test-automation")
            || string.IsNullOrWhiteSpace(pipeName)) {
            return;
        }

        typedCommandRouter = CreateTypedCommandRouter();
        typedControlServer = new NamedPipeControlServer(pipeName, HandleTypedRequestAsync);
        _ = typedControlServer.StartAsync(cts.Token);
        Log.Information(
            $"[Survivalcraft.TestAutomation] Typed control protocol v{ControlProtocol.CurrentVersion} listening. Role={nodeRole}.");
    }

    private void StopTypedControlServer() {
        NamedPipeControlServer? server = typedControlServer;
        typedControlServer = null;
        if (server == null) {
            return;
        }
        try {
            server.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(1));
        }
        catch {
            // Process shutdown should not be blocked by a disconnected test controller.
        }
    }

    private async ValueTask<ControlResponse> HandleTypedRequestAsync(
        ControlRequest request,
        CancellationToken cancellationToken) {
        TaskCompletionSource<ControlResponse> completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.Dispatch(() => {
            try {
                completion.TrySetResult(ProcessTypedRequest(request));
            }
            catch (Exception exception) {
                completion.TrySetResult(ControlProtocol.Failure(
                    request,
                    "command_failed",
                    exception.Message,
                    exception.ToString(),
                    Interlocked.Increment(ref typedResponseSequence)));
            }
        });

        try {
            return await completion.Task
                .WaitAsync(TimeSpan.FromSeconds(30), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException exception) {
            return ControlProtocol.Failure(
                request,
                "main_thread_timeout",
                "The game main thread did not complete the command in time.",
                exception.ToString(),
                Interlocked.Increment(ref typedResponseSequence));
        }
    }

    private ControlResponse ProcessTypedRequest(ControlRequest request) {
        long sequence = Interlocked.Increment(ref typedResponseSequence);
        TypedCommandRouter router = typedCommandRouter
            ?? throw new InvalidOperationException("Typed command router is not initialized.");
        return router.Dispatch(request, sequence, nodeRole);
    }

    private PingResult BuildPingResult(PingCommand command) =>
        new(
            command.Message ?? "pong",
            nodeRole,
            Environment.ProcessId,
            ControlProtocol.CurrentVersion,
            DateTimeOffset.UtcNow);

    private ControlAck ConfigureNode(ConfigureNodeCommand command) {
        nodeRole = command.Role;
        SettingsManager.MusicVolume = command.DisableAudio ? 0 : SettingsManager.MusicVolume;
        SettingsManager.SoundsVolume = command.DisableAudio ? 0 : SettingsManager.SoundsVolume;
        SettingsManager.FileAssociationEnabled = false;
        SettingsManager.LetRamdomStatic = command.FixedRandom;
        SettingsManager.MultithreadedTerrainUpdate = !command.DisableMultithreadedTerrain;
        if (command.FixedRandom) {
            global::Game.Random.Counter = command.Seed;
        }
        if (command.NetworkIdentity != null) {
            if (!Guid.TryParse(command.NetworkIdentity, out Guid networkIdentity)
                || networkIdentity == Guid.Empty) {
                throw new ArgumentException(
                    "NetworkIdentity must be a non-empty GUID.",
                    nameof(command.NetworkIdentity));
            }
            SettingsManager.OnlineAccessToken = networkIdentity.ToString("D");
        }
        NetworkCompatibilityRuntime.ClearLocalManifestOverride();
        ModNetworkManager.ClearLocalCatalogOverride();
        NetworkSessionLifecycleTracker.Reset();
        ResetTestModMessages();
        ResetTypedPlayerActions();
        actionEventJournal.Reset();
        return new ControlAck(true, $"Configured role '{nodeRole}' with seed {command.Seed}.");
    }

    private CreateWorldResult CreateWorld(CreateWorldCommand command) {
        if (!LoadingScreen.m_isContentLoaded) {
            throw new InvalidOperationException("Game content is not loaded yet.");
        }
        ReturnToMainMenu(saveWorld: false);
        SettingsManager.ShowPlayWithFriendsDialog = false;
        SettingsManager.PlayWithFriendsEnabled = false;
        WorldSettings settings = new() {
            Name = command.Name,
            Seed = command.Seed,
            GameMode = command.Creative ? GameMode.Creative : GameMode.Survival,
            TerrainGenerationMode = command.FlatTerrain
                ? TerrainGenerationMode.FlatContinent
                : TerrainGenerationMode.Continent,
            OriginalSerializationVersion = VersionsManager.SerializationVersion,
            EnvironmentBehaviorMode = EnvironmentBehaviorMode.Static,
            TimeOfDayMode = TimeOfDayMode.Day,
            AreWeatherEffectsEnabled = false,
            AreSeasonsChanging = false
        };
        WorldInfo world = WorldsManager.CreateWorld(settings);
        ScreensManager.SwitchScreen("GameLoading", world, null, false);
        return new CreateWorldResult(
            world.DirectoryName,
            world.WorldSettings.Name,
            world.WorldSettings.WorldSeed);
    }

    private ControlAck ReloadWorld(ReloadWorldCommand command) {
        ReturnToMainMenu(saveWorld: false);
        WorldsManager.UpdateWorldsList();
        WorldInfo? world = WorldsManager.WorldInfos.FirstOrDefault(candidate =>
            string.Equals(
                candidate.DirectoryName,
                command.DirectoryName,
                StringComparison.OrdinalIgnoreCase));
        if (world == null) {
            throw new InvalidOperationException(
                $"World '{command.DirectoryName}' was not found.");
        }
        ScreensManager.SwitchScreen("GameLoading", world, null, false);
        return new ControlAck(true, world.DirectoryName);
    }

    private SaveWorldResult SaveWorld(SaveWorldCommand command) {
        if (GameManager.Project == null || GameManager.IsNetworkProject) {
            return new SaveWorldResult(string.Empty, false);
        }
        string directoryName = GameManager.WorldInfo?.DirectoryName ?? string.Empty;
        GameManager.SaveProject(command.WaitForCompletion, showErrorDialog: false);
        return new SaveWorldResult(directoryName, true);
    }

    private ControlAck ConnectClient(ConnectClientCommand command) {
        ReturnToMainMenu(saveWorld: false);
        IPAddress address = IPAddress.Parse(command.Host);
        ScreensManager.SwitchScreen(
            "GameLoading",
            null,
            null,
            new IPEndPoint(address, command.Port),
            null);
        return new ControlAck(true, $"Connecting to {address}:{command.Port}.");
    }

    private ControlAck StartRelayHost(StartRelayHostCommand command) {
        if (GameManager.Project == null || GameManager.IsNetworkProject) {
            throw new InvalidOperationException(
                "Starting a relay host requires a loaded storage-backed server world.");
        }
        if (NetworkManager.RelayHost != null) {
            throw new InvalidOperationException("A relay host is already active.");
        }
        IPAddress address = IPAddress.Parse(command.Host);
        NetworkManager.StartRelayHost(new IPEndPoint(address, command.Port));
        return new ControlAck(true, $"Creating relay room through {address}:{command.Port}.");
    }

    private ControlAck ConnectRelay(ConnectRelayCommand command) {
        if (string.IsNullOrWhiteSpace(command.RoomCode)) {
            throw new ArgumentException("Relay room code cannot be empty.", nameof(command));
        }
        ReturnToMainMenu(saveWorld: false);
        IPAddress address = IPAddress.Parse(command.Host);
        RelayJoinInfo joinInfo = new(
            new IPEndPoint(address, command.Port),
            command.RoomCode.Trim());
        ScreensManager.SwitchScreen(
            "GameLoading",
            null,
            null,
            joinInfo,
            null);
        return new ControlAck(
            true,
            $"Joining relay room '{joinInfo.RoomCode}' through {address}:{command.Port}.");
    }

    private static RelayStatus BuildRelayStatus() {
        RelayHost? host = NetworkManager.RelayHost as RelayHost;
        RelayClient? client = NetworkManager.RelayClient as RelayClient;
        bool hostConnected = host?.IsConnected == true;
        bool clientConnected = client?.IsConnected == true;
        string mode = hostConnected && clientConnected
            ? "host+client"
            : hostConnected
                ? "host"
                : clientConnected
                    ? "client"
                    : "none";
        return new RelayStatus(
            mode,
            hostConnected,
            clientConnected,
            NetworkManager.RelayHostCodeStatus.ToString(),
            host?.RoomCode ?? string.Empty,
            host?.GlobalCode ?? string.Empty,
            host?.Sessions.Count() ?? 0);
    }

    private ControlAck EnterWorld(EnterWorldCommand command) {
        if (ScreensManager.CurrentScreen is not PlayerScreen playerScreen) {
            throw new InvalidOperationException(
                $"EnterWorld requires PlayerScreen, current screen is {ScreensManager.CurrentScreen?.GetType().Name ?? "null"}.");
        }
        playerScreen.m_nameTextBox.Text = command.PlayerName;
        playerScreen.m_playerData.Name = command.PlayerName;
        if (!playerScreen.m_playButton.IsVisible || !playerScreen.m_playButton.IsEnabled) {
            throw new InvalidOperationException("Player screen Play button is not ready.");
        }
        if (!playerScreen.TryEnterWorld()) {
            throw new InvalidOperationException("Player screen rejected the enter-world request.");
        }
        return new ControlAck(true, $"Entering world as '{command.PlayerName}'.");
    }

    private ControlAck Disconnect(DisconnectCommand command) {
        ReturnToMainMenu(command.SaveWorld);
        return new ControlAck(true, "Network and project stopped.");
    }

    private TerrainMutationResult ChangeTerrainCell(ChangeTerrainCellCommand command) {
        Project project = GameManager.Project
            ?? throw new InvalidOperationException("No project is loaded.");
        SubsystemTerrain terrain = project.FindSubsystem<SubsystemTerrain>(true);
        SubsystemPlayers players = project.FindSubsystem<SubsystemPlayers>(true);
        Vector3 basePosition = players.MainPlayerData?.ComponentPlayer?.ComponentBody?.Position
            ?? players.MainPlayerData?.SpawnPosition
            ?? players.GlobalSpawnPosition;
        int x = command.X ?? Terrain.ToCell(basePosition.X) + 6;
        int y = command.Y ?? Math.Clamp(Terrain.ToCell(basePosition.Y) + 3, 2, TerrainChunk.Height - 2);
        int z = command.Z ?? Terrain.ToCell(basePosition.Z) + 2;
        int previous = terrain.Terrain.GetCellValue(x, y, z);
        int contents = command.Block switch {
            TestBlockKind.Air => 0,
            TestBlockKind.Dirt => DirtBlock.Index,
            TestBlockKind.Glass => GlassBlock.Index,
            _ => throw new ArgumentOutOfRangeException(nameof(command.Block))
        };
        int value = contents == 0
            ? 0
            : Terrain.MakeBlockValue(contents, 0, command.Data);
        terrain.ChangeCell(x, y, z, value);
        int actual = terrain.Terrain.GetCellValue(x, y, z);
        return new TerrainMutationResult(ToTerrainCellState(x, y, z, actual), previous);
    }

    private StateSnapshot BuildSnapshot(GetSnapshotCommand command) {
        Project? project = GameManager.Project;
        SubsystemPlayers? players = project?.FindSubsystem<SubsystemPlayers>();
        PlayerState[] playerStates = players?.PlayersData
            .Select(player => {
                ComponentBody? body = player.ComponentPlayer?.ComponentBody;
                return new PlayerState(
                    player.PlayerIndex,
                    player.Name ?? string.Empty,
                    player.IsMainPlayer,
                    player.IsReadyForPlaying,
                    body == null ? null : ToVector(body.Position),
                    body == null ? null : ToVector(body.Velocity),
                    player.ComponentPlayer?.ComponentHealth?.Health);
            })
            .ToArray() ?? [];

        EntityState[] entityStates = command.IncludeEntities && project != null
            ? project.Entities.Select(entity => {
                ComponentBody? body = entity.FindComponent<ComponentBody>();
                return new EntityState(
                    entity.Id,
                    entity.ValuesDictionary?.DatabaseObject?.Name ?? string.Empty,
                    body == null ? null : ToVector(body.Position),
                    body == null ? null : ToVector(body.Velocity));
            }).ToArray()
            : [];

        SubsystemTerrain? terrain = project?.FindSubsystem<SubsystemTerrain>();
        TerrainCellState[] cells = terrain == null || command.TerrainCells == null
            ? []
            : command.TerrainCells.Select(point => {
                int value = terrain.Terrain.GetCellValue(point.X, point.Y, point.Z);
                return ToTerrainCellState(point.X, point.Y, point.Z, value);
            }).ToArray();

        WorldInfo? worldInfo = GameManager.WorldInfo;
        WorldState? world = worldInfo == null
            ? null
            : new WorldState(
                worldInfo.WorldSettings?.Name ?? string.Empty,
                worldInfo.DirectoryName ?? string.Empty,
                worldInfo.WorldSettings?.WorldSeed ?? 0);
        NetworkFaultMetrics metrics = NetworkFaultInjector.Metrics;
        ModNetworkRuntimeMetrics modMetrics = ModNetworkManager.Metrics;
        Dictionary<string, string> diagnostics = new() {
            ["projectKind"] = GameManager.IsNetworkProject ? "network" : "storage",
            ["fault.sent"] = metrics.Sent.ToString(),
            ["fault.dropped"] = metrics.Dropped.ToString(),
            ["fault.duplicated"] = metrics.Duplicated.ToString(),
            ["fault.pending"] = metrics.Pending.ToString(),
            ["compatibility.state"] = BuildCompatibilityStateName(),
            ["compatibility.code"] = NetworkCompatibilityRuntime.LastClientResult?.PrimaryCode.ToString() ?? string.Empty,
            ["compatibility.summary"] = NetworkCompatibilityRuntime.LastClientResult?.Summary ?? string.Empty,
            ["modChannel.sent"] = modMetrics.Sent.ToString(),
            ["modChannel.received"] = modMetrics.Received.ToString(),
            ["modChannel.delivered"] = modMetrics.Delivered.ToString(),
            ["modChannel.dropped"] = modMetrics.Dropped.ToString(),
            ["modChannel.handlerErrors"] = modMetrics.HandlerErrors.ToString()
        };

        return new StateSnapshot(
            1,
            nodeRole,
            Time.FrameIndex,
            Time.RealTime,
            ScreensManager.CurrentScreen?.GetType().Name ?? "null",
            NetworkManager.MyState.ToString(),
            world,
            playerStates,
            entityStates,
            cells,
            diagnostics);
    }

    private ClockState SetClock(SetClockCommand command) {
        if (command.Manual) {
            Time.EnableManualTime(command.InitialSeconds);
            GameManager.IsProjectAutoUpdateEnabled = false;
        }
        else {
            Time.DisableManualTime();
            GameManager.IsProjectAutoUpdateEnabled = true;
        }
        return new ClockState(Time.IsManualTime, Time.FrameIndex, Time.RealTime);
    }

    private ClockState AdvanceTicks(AdvanceTicksCommand command) {
        if (!Time.IsManualTime) {
            throw new InvalidOperationException("Manual time must be enabled before advancing ticks.");
        }
        if (command.Ticks < 0 || command.SecondsPerTick < 0) {
            throw new ArgumentOutOfRangeException(nameof(command.Ticks));
        }
        for (int index = 0; index < command.Ticks; index++) {
            Time.AdvanceManualTime(command.SecondsPerTick);
            GameManager.UpdateProjectFixed(1, (float)command.SecondsPerTick);
            NetworkManager.Update();
        }
        return new ClockState(true, Time.FrameIndex, Time.RealTime);
    }

    private NetworkFaultState SetNetworkFaults(SetNetworkFaultsCommand command) {
        NetworkFaultProfile profile = command.Profile.Validate();
        NetworkFaultInjector.Configure(new NetworkFaultSettings(
            profile.Seed,
            profile.BaseLatencyMilliseconds,
            profile.JitterMilliseconds,
            profile.LossRate,
            profile.DuplicationRate,
            profile.ReorderRate,
            profile.ReorderDelayMilliseconds,
            profile.BandwidthBytesPerSecond,
            profile.TargetPacketTypes,
            profile.GuaranteeReliableLoss));
        if (profile.GuaranteeReliableLoss) {
            QueueApplicationReliabilityProbe();
        }
        return BuildNetworkFaultState();
    }

    private CompatibilityStatus ConfigureCompatibility(
        ConfigureCompatibilityCommand command) {
        if (command.ClearOverride) {
            NetworkCompatibilityRuntime.ClearLocalManifestOverride();
            return BuildCompatibilityStatus();
        }

        NetworkCompatibilityManifest loaded =
            NetworkCompatibilityRuntime.CreateLoadedManifest();
        IEnumerable<NetworkModIdentity> mods = command.Mods == null
            ? loaded.Mods
            : command.Mods.Select(mod => new NetworkModIdentity(
                mod.PackageName,
                mod.Version,
                mod.ApiVersion));
        NetworkCompatibilityManifest manifest = new(
            command.ProtocolRevision ?? loaded.ProtocolRevision,
            command.ApiVersion ?? loaded.ApiVersion,
            command.Capabilities.HasValue
                ? (NetworkCapabilities)command.Capabilities.Value
                : loaded.Capabilities,
            mods);
        NetworkCompatibilityRuntime.SetLocalManifestOverride(manifest);
        return BuildCompatibilityStatus();
    }

    private CompatibilityStatus BuildCompatibilityStatus() {
        NetworkCompatibilityManifest manifest =
            NetworkCompatibilityRuntime.GetLocalManifest();
        NetworkCompatibilityResult? result =
            NetworkCompatibilityRuntime.LastClientResult;
        return new CompatibilityStatus(
            BuildCompatibilityStateName(),
            NetworkCompatibilityRuntime.HasLocalManifestOverride,
            manifest.ProtocolRevision,
            manifest.ApiVersion ?? string.Empty,
            (int)manifest.Capabilities,
            (manifest.Mods ?? []).Select(mod => new CompatibilityModDescriptor(
                mod.PackageName ?? string.Empty,
                mod.Version ?? string.Empty,
                mod.ApiVersion ?? string.Empty)).ToArray(),
            result?.Accepted,
            result?.PrimaryCode.ToString(),
            result?.Summary,
            (result?.Issues ?? []).Select(issue => new CompatibilityIssueState(
                issue.Code.ToString(),
                issue.PackageName ?? string.Empty,
                issue.Expected ?? string.Empty,
                issue.Actual ?? string.Empty,
                issue.Message ?? string.Empty)).ToArray());
    }

    private static string BuildCompatibilityStateName() {
        ClientSession? session = NetworkManager.MySession;
        if (session != null) {
            return session.CompatibilityState.ToString();
        }
        NetworkCompatibilityResult? result =
            NetworkCompatibilityRuntime.LastClientResult;
        if (result != null) {
            return result.Accepted
                ? NetworkCompatibilityState.Accepted.ToString()
                : NetworkCompatibilityState.Rejected.ToString();
        }
        return NetworkCompatibilityState.NotStarted.ToString();
    }

    private ControlAck QueueTypedWidgetClick(ClickWidgetCommand command) {
        pendingWidgetClicks.Enqueue("CLICK_WIDGET " + command.NameOrText);
        return new ControlAck(true, command.NameOrText);
    }

    private object? ExecuteTypedLegacyScript(ExecuteLegacyScriptCommand command) {
        object? result = ExecuteCommandScript(command.Code);
        return command.ReturnValue ? result : new ControlAck(true);
    }

    private NodeStatus BuildNodeStatus() {
        Project? project = GameManager.Project;
        SubsystemPlayers? players = project?.FindSubsystem<SubsystemPlayers>();
        PlayerData? mainPlayer = players?.MainPlayerData;
        return new NodeStatus(
            nodeRole,
            ScreensManager.CurrentScreen?.GetType().Name ?? "null",
            LoadingScreen.m_isContentLoaded,
            project != null,
            NetworkManager.IsServerRunning,
            NetworkManager.IsClientRunning,
            NetworkManager.IsRuntimeBound,
            NetworkManager.MyState.ToString(),
            NetworkManager.PlayerIndex,
            players?.PlayersData.Count ?? 0,
            NetworkManager.ServerSessions.Count(),
            mainPlayer?.IsReadyForPlaying == true && mainPlayer.ComponentPlayer != null,
            Time.FrameIndex,
            Time.RealTime,
            BuildNetworkFaultState(),
            BuildCompatibilityStateName(),
            NetworkCompatibilityRuntime.LastClientResult?.PrimaryCode.ToString(),
            NetworkManager.MySession?.AdmissionState.ToString()
                ?? NetworkSessionAdmissionState.NotStarted.ToString(),
            NetworkManager.MySession?.AdmissionResult?.Code.ToString()
                ?? NetworkSessionAdmissionRuntime.LastClientResult?.Code.ToString(),
            NetworkManager.MySession?.SessionId.ToString("D"));
    }

    private NetworkFaultState BuildNetworkFaultState() {
        NetworkFaultSettings settings = NetworkFaultInjector.Settings;
        NetworkFaultMetrics metrics = NetworkFaultInjector.Metrics;
        ApplicationReliabilityMetrics reliability =
            ApplicationReliabilityRuntime.Metrics;
        return new NetworkFaultState(
            new NetworkFaultProfile(
                settings.Seed,
                settings.BaseLatencyMilliseconds,
                settings.JitterMilliseconds,
                settings.LossRate,
                settings.DuplicationRate,
                settings.ReorderRate,
                settings.ReorderDelayMilliseconds,
                settings.BandwidthBytesPerSecond,
                settings.TargetPacketTypes,
                settings.GuaranteeReliableLoss),
            metrics.Sent,
            metrics.Dropped,
            metrics.Duplicated,
            metrics.Pending,
            reliability.Created,
            reliability.Retried,
            reliability.Acknowledged,
            reliability.DuplicateDeliveries,
            reliability.TimedOut,
            reliability.Failed,
            reliability.Pending,
            reliability.PendingBytes);
    }

    private void ReturnToMainMenu(bool saveWorld) {
        if (saveWorld && GameManager.Project != null && !GameManager.IsNetworkProject) {
            GameManager.SaveProject(waitForCompletion: true, showErrorDialog: false);
        }
        NetworkManager.Stop();
        GameManager.DisposeProject();
        DialogsManager.HideAllDialogs();
        if (LoadingScreen.m_isContentLoaded
            && ScreensManager.CurrentScreen is not MainMenuScreen) {
            ScreensManager.SwitchScreen("MainMenu");
        }
    }

    private static Vector3State ToVector(Vector3 vector) =>
        new(vector.X, vector.Y, vector.Z);

    private static TerrainCellState ToTerrainCellState(
        int x,
        int y,
        int z,
        int value) =>
        new(
            x,
            y,
            z,
            Terrain.ReplaceLight(value, 0),
            Terrain.ExtractContents(value),
            Terrain.ExtractData(value));

    private static string? GetArgument(string[] args, string name) {
        for (int index = 0; index < args.Length - 1; index++) {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase)) {
                return args[index + 1];
            }
        }
        return null;
    }

    private static bool HasSwitch(string[] args, string name) =>
        args.Any(argument =>
            string.Equals(argument, name, StringComparison.OrdinalIgnoreCase));
}
