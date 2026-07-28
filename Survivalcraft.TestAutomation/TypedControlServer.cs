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
    private long typedResponseSequence;
    private string nodeRole = "unknown";

    private void StartTypedControlServer() {
        string[] args = Environment.GetCommandLineArgs();
        string? pipeName = GetArgument(args, "--control-pipe");
        nodeRole = GetArgument(args, "--role") ?? "unknown";
        if (string.IsNullOrWhiteSpace(pipeName)) {
            return;
        }

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
        return request.Command switch {
            ControlCommandNames.Ping => ControlProtocol.Success(
                request,
                BuildPingResult(request.DeserializePayload<PingCommand>()),
                sequence),
            ControlCommandNames.GetStatus => ControlProtocol.Success(
                request,
                BuildNodeStatus(),
                sequence),
            ControlCommandNames.ConfigureNode => ControlProtocol.Success(
                request,
                ConfigureNode(request.DeserializePayload<ConfigureNodeCommand>()),
                sequence),
            ControlCommandNames.CreateWorld => ControlProtocol.Success(
                request,
                CreateWorld(request.DeserializePayload<CreateWorldCommand>()),
                sequence),
            ControlCommandNames.ReloadWorld => ControlProtocol.Success(
                request,
                ReloadWorld(request.DeserializePayload<ReloadWorldCommand>()),
                sequence),
            ControlCommandNames.SaveWorld => ControlProtocol.Success(
                request,
                SaveWorld(request.DeserializePayload<SaveWorldCommand>()),
                sequence),
            ControlCommandNames.ConnectClient => ControlProtocol.Success(
                request,
                ConnectClient(request.DeserializePayload<ConnectClientCommand>()),
                sequence),
            ControlCommandNames.EnterWorld => ControlProtocol.Success(
                request,
                EnterWorld(request.DeserializePayload<EnterWorldCommand>()),
                sequence),
            ControlCommandNames.Disconnect => ControlProtocol.Success(
                request,
                Disconnect(request.DeserializePayload<DisconnectCommand>()),
                sequence),
            ControlCommandNames.ChangeTerrainCell => ControlProtocol.Success(
                request,
                ChangeTerrainCell(request.DeserializePayload<ChangeTerrainCellCommand>()),
                sequence),
            ControlCommandNames.GetSnapshot => ControlProtocol.Success(
                request,
                BuildSnapshot(request.DeserializePayload<GetSnapshotCommand>()),
                sequence),
            ControlCommandNames.SetClock => ControlProtocol.Success(
                request,
                SetClock(request.DeserializePayload<SetClockCommand>()),
                sequence),
            ControlCommandNames.AdvanceTicks => ControlProtocol.Success(
                request,
                AdvanceTicks(request.DeserializePayload<AdvanceTicksCommand>()),
                sequence),
            ControlCommandNames.SetNetworkFaults => ControlProtocol.Success(
                request,
                SetNetworkFaults(request.DeserializePayload<SetNetworkFaultsCommand>()),
                sequence),
            ControlCommandNames.ClickWidget => ControlProtocol.Success(
                request,
                QueueTypedWidgetClick(request.DeserializePayload<ClickWidgetCommand>()),
                sequence),
            ControlCommandNames.ExecuteLegacyScript => ControlProtocol.Success(
                request,
                ExecuteTypedLegacyScript(request.DeserializePayload<ExecuteLegacyScriptCommand>()),
                sequence),
            _ => ControlProtocol.Failure(
                request,
                "unknown_command",
                $"Unknown control command '{request.Command}'.",
                null,
                sequence)
        };
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
        pendingWidgetClicks.Enqueue("CLICK_WIDGET PlayButton");
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
        Dictionary<string, string> diagnostics = new() {
            ["projectKind"] = GameManager.IsNetworkProject ? "network" : "storage",
            ["fault.sent"] = metrics.Sent.ToString(),
            ["fault.dropped"] = metrics.Dropped.ToString(),
            ["fault.duplicated"] = metrics.Duplicated.ToString(),
            ["fault.pending"] = metrics.Pending.ToString()
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
            profile.BandwidthBytesPerSecond));
        return BuildNetworkFaultState();
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
            NetworkManager.MyState.ToString(),
            NetworkManager.PlayerIndex,
            players?.PlayersData.Count ?? 0,
            NetworkManager.ServerSessions.Count(),
            mainPlayer?.IsReadyForPlaying == true && mainPlayer.ComponentPlayer != null,
            Time.FrameIndex,
            Time.RealTime,
            BuildNetworkFaultState());
    }

    private NetworkFaultState BuildNetworkFaultState() {
        NetworkFaultSettings settings = NetworkFaultInjector.Settings;
        NetworkFaultMetrics metrics = NetworkFaultInjector.Metrics;
        return new NetworkFaultState(
            new NetworkFaultProfile(
                settings.Seed,
                settings.BaseLatencyMilliseconds,
                settings.JitterMilliseconds,
                settings.LossRate,
                settings.DuplicationRate,
                settings.ReorderRate,
                settings.ReorderDelayMilliseconds,
                settings.BandwidthBytesPerSecond),
            metrics.Sent,
            metrics.Dropped,
            metrics.Duplicated,
            metrics.Pending);
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
}
