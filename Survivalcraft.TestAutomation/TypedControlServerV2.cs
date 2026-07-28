using System.Globalization;
using Engine;
using Game;
using Game.Network;
using GameEntitySystem;
using Survivalcraft.Testing.Control;
using Survivalcraft.Testing.Networking;
using Survivalcraft.Testing.Snapshots;

namespace Survivalcraft.TestAutomation;

public partial class TestAutomationModLoader {
    private TypedCommandRouter CreateTypedCommandRouter() {
        TypedCommandRouter router = new();

        router.Register<EmptyCommand, AutomationDescription>(
            Metadata(ControlCommandNames.Describe, AutomationCommandDomain.System),
            (_, _) => BuildAutomationDescription(router));
        router.Register<PingCommand, PingResult>(
            Metadata(ControlCommandNames.Ping, AutomationCommandDomain.System),
            (_, command) => BuildPingResult(command));
        router.Register<EmptyCommand, NodeStatus>(
            Metadata(ControlCommandNames.GetStatus, AutomationCommandDomain.Node),
            (_, _) => BuildNodeStatus());
        router.Register<ConfigureNodeCommand, ControlAck>(
            Metadata(ControlCommandNames.ConfigureNode, AutomationCommandDomain.Node, true),
            (_, command) => ConfigureNode(command));
        router.Register<CreateWorldCommand, CreateWorldResult>(
            Metadata(ControlCommandNames.CreateWorld, AutomationCommandDomain.World, true),
            (_, command) => CreateWorld(command));
        router.Register<ReloadWorldCommand, ControlAck>(
            Metadata(ControlCommandNames.ReloadWorld, AutomationCommandDomain.World, true),
            (_, command) => ReloadWorld(command));
        router.Register<SaveWorldCommand, SaveWorldResult>(
            Metadata(
                ControlCommandNames.SaveWorld,
                AutomationCommandDomain.World,
                true,
                requiresProject: true),
            (_, command) => SaveWorld(command));
        router.Register<ConnectClientCommand, ControlAck>(
            Metadata(ControlCommandNames.ConnectClient, AutomationCommandDomain.Network, true),
            (_, command) => ConnectClient(command));
        router.Register<StartRelayHostCommand, ControlAck>(
            Metadata(
                ControlCommandNames.StartRelayHost,
                AutomationCommandDomain.Network,
                true,
                requiresProject: true),
            (_, command) => StartRelayHost(command));
        router.Register<ConnectRelayCommand, ControlAck>(
            Metadata(ControlCommandNames.ConnectRelay, AutomationCommandDomain.Network, true),
            (_, command) => ConnectRelay(command));
        router.Register<EmptyCommand, RelayStatus>(
            Metadata(ControlCommandNames.GetRelayStatus, AutomationCommandDomain.Network),
            (_, _) => BuildRelayStatus());
        router.Register<EnterWorldCommand, ControlAck>(
            Metadata(ControlCommandNames.EnterWorld, AutomationCommandDomain.Player, true),
            (_, command) => EnterWorld(command));
        router.Register<DisconnectCommand, ControlAck>(
            Metadata(ControlCommandNames.Disconnect, AutomationCommandDomain.Network, true),
            (_, command) => Disconnect(command));
        router.Register<ChangeTerrainCellCommand, TerrainMutationResult>(
            Metadata(
                ControlCommandNames.ChangeTerrainCell,
                AutomationCommandDomain.Terrain,
                true,
                requiresProject: true),
            (_, command) => ChangeTerrainCell(command));
        router.Register<GetSnapshotCommand, StateSnapshot>(
            Metadata(ControlCommandNames.GetSnapshot, AutomationCommandDomain.State),
            (_, command) => BuildSnapshot(command));
        router.Register<GetSnapshotV2Command, StateSnapshotV2>(
            Metadata(ControlCommandNames.GetSnapshotV2, AutomationCommandDomain.State),
            (_, command) => BuildSnapshotV2(command));
        router.Register<GetEventsCommand, ActionEventsResult>(
            Metadata(ControlCommandNames.GetEvents, AutomationCommandDomain.State),
            (_, command) => actionEventJournal.Query(command));
        router.Register<EmptyCommand, ResetEventsResult>(
            Metadata(ControlCommandNames.ResetEvents, AutomationCommandDomain.State, true),
            (_, _) => actionEventJournal.Reset());
        router.Register<SetCreativeFlyCommand, ActionReceipt>(
            Metadata(
                ControlCommandNames.SetCreativeFly,
                AutomationCommandDomain.Player,
                true,
                allowedRoles: ["client", "client-a", "client-b"],
                requiresProject: true,
                requiresNetwork: true),
            SetCreativeFly);
        router.Register<PreparePlayerMotionFixtureCommand, PlayerMotionFixture>(
            Metadata(
                ControlCommandNames.PreparePlayerMotionFixture,
                AutomationCommandDomain.Fixture,
                true,
                allowedRoles: ["server"],
                requiresProject: true,
                requiresNetwork: true,
                stability: AutomationCommandStability.Experimental),
            (_, command) => PreparePlayerMotionFixture(command));
        router.Register<ApplyPlayerMotionFixtureCommand, ControlAck>(
            Metadata(
                ControlCommandNames.ApplyPlayerMotionFixture,
                AutomationCommandDomain.Fixture,
                true,
                allowedRoles: ["client", "client-a", "client-b", "client-c"],
                requiresProject: true,
                requiresNetwork: true,
                stability: AutomationCommandStability.Experimental),
            (_, command) => ApplyPlayerMotionFixture(command));
        router.Register<PlayerMoveCommand, ActionReceipt>(
            Metadata(
                ControlCommandNames.PlayerMove,
                AutomationCommandDomain.Player,
                true,
                allowedRoles: ["client", "client-a", "client-b", "client-c"],
                requiresProject: true,
                requiresNetwork: true,
                stability: AutomationCommandStability.Experimental),
            QueuePlayerMove);
        router.Register<PlayerLookCommand, ActionReceipt>(
            Metadata(
                ControlCommandNames.PlayerLook,
                AutomationCommandDomain.Player,
                true,
                allowedRoles: ["client", "client-a", "client-b", "client-c"],
                requiresProject: true,
                requiresNetwork: true,
                stability: AutomationCommandStability.Experimental),
            QueuePlayerLook);
        router.Register<PlayerSetCrouchCommand, ActionReceipt>(
            Metadata(
                ControlCommandNames.PlayerSetCrouch,
                AutomationCommandDomain.Player,
                true,
                allowedRoles: ["client", "client-a", "client-b", "client-c"],
                requiresProject: true,
                requiresNetwork: true,
                stability: AutomationCommandStability.Experimental),
            QueuePlayerSetCrouch);
        router.Register<PrepareTerrainInteractionFixtureCommand, TerrainInteractionFixture>(
            Metadata(
                ControlCommandNames.PrepareTerrainInteractionFixture,
                AutomationCommandDomain.Fixture,
                true,
                allowedRoles: ["server"],
                requiresProject: true,
                requiresNetwork: true,
                stability: AutomationCommandStability.Experimental),
            (_, command) => PrepareTerrainInteractionFixture(command));
        router.Register<ApplyTerrainInteractionFixtureCommand, ControlAck>(
            Metadata(
                ControlCommandNames.ApplyTerrainInteractionFixture,
                AutomationCommandDomain.Fixture,
                true,
                allowedRoles: ["client", "client-a", "client-b"],
                requiresProject: true,
                requiresNetwork: true,
                stability: AutomationCommandStability.Experimental),
            (_, command) => ApplyTerrainInteractionFixture(command));
        router.Register<PlayerUseCommand, ActionReceipt>(
            Metadata(
                ControlCommandNames.PlayerUse,
                AutomationCommandDomain.Player,
                true,
                allowedRoles: ["client", "client-a", "client-b"],
                requiresProject: true,
                requiresNetwork: true,
                stability: AutomationCommandStability.Experimental),
            QueuePlayerUse);
        router.Register<PlayerPlaceCommand, ActionReceipt>(
            Metadata(
                ControlCommandNames.PlayerPlace,
                AutomationCommandDomain.Player,
                true,
                allowedRoles: ["client", "client-a", "client-b"],
                requiresProject: true,
                requiresNetwork: true,
                stability: AutomationCommandStability.Experimental),
            QueuePlayerPlace);
        router.Register<PlayerDigCommand, ActionReceipt>(
            Metadata(
                ControlCommandNames.PlayerDig,
                AutomationCommandDomain.Player,
                true,
                allowedRoles: ["client", "client-a", "client-b"],
                requiresProject: true,
                requiresNetwork: true,
                stability: AutomationCommandStability.Experimental),
            QueuePlayerDig);
        router.Register<PrepareTerrainStreamingFixtureCommand, TerrainStreamingFixture>(
            Metadata(
                ControlCommandNames.PrepareTerrainStreamingFixture,
                AutomationCommandDomain.Fixture,
                true,
                allowedRoles: ["server"],
                requiresProject: true,
                requiresNetwork: true,
                stability: AutomationCommandStability.Experimental),
            (_, command) => PrepareTerrainStreamingFixture(command));
        router.Register<ApplyTerrainStreamingViewCommand, ControlAck>(
            Metadata(
                ControlCommandNames.ApplyTerrainStreamingView,
                AutomationCommandDomain.Fixture,
                true,
                allowedRoles: ["client", "client-a", "client-b", "client-c"],
                requiresProject: true,
                requiresNetwork: true,
                stability: AutomationCommandStability.Experimental),
            (_, command) => ApplyTerrainStreamingView(command));
        router.Register<RunTerrainRevisionBurstCommand, TerrainRevisionBurstResult>(
            Metadata(
                ControlCommandNames.RunTerrainRevisionBurst,
                AutomationCommandDomain.Terrain,
                true,
                allowedRoles: ["server"],
                requiresProject: true,
                requiresNetwork: true,
                stability: AutomationCommandStability.Experimental),
            RunTerrainRevisionBurst);
        router.Register<SetClockCommand, ClockState>(
            Metadata(ControlCommandNames.SetClock, AutomationCommandDomain.Time, true),
            (_, command) => SetClock(command));
        router.Register<AdvanceTicksCommand, ClockState>(
            Metadata(ControlCommandNames.AdvanceTicks, AutomationCommandDomain.Time, true),
            (_, command) => AdvanceTicks(command));
        router.Register<SetNetworkFaultsCommand, NetworkFaultState>(
            Metadata(ControlCommandNames.SetNetworkFaults, AutomationCommandDomain.Network, true),
            (_, command) => SetNetworkFaults(command));
        router.Register<ConfigureCompatibilityCommand, CompatibilityStatus>(
            Metadata(ControlCommandNames.ConfigureCompatibility, AutomationCommandDomain.Network, true),
            (_, command) => ConfigureCompatibility(command));
        router.Register<EmptyCommand, CompatibilityStatus>(
            Metadata(ControlCommandNames.GetCompatibility, AutomationCommandDomain.Network),
            (_, _) => BuildCompatibilityStatus());
        router.Register<ConfigureModChannelsCommand, ModChannelStatus>(
            Metadata(ControlCommandNames.ConfigureModChannels, AutomationCommandDomain.Network, true),
            (_, command) => ConfigureModChannels(command));
        router.Register<EmptyCommand, ModChannelStatus>(
            Metadata(ControlCommandNames.GetModChannels, AutomationCommandDomain.Network),
            (_, _) => BuildModChannelStatus());
        router.Register<SendTestModMessageCommand, TestModSendResult>(
            Metadata(ControlCommandNames.SendTestModMessage, AutomationCommandDomain.Network, true),
            (_, command) => SendTestModMessage(command));
        router.Register<EmptyCommand, TestModMessagesStatus>(
            Metadata(ControlCommandNames.GetTestModMessages, AutomationCommandDomain.Network),
            (_, _) => BuildTestModMessagesStatus());
        router.Register<EmptyCommand, TestModMessagesStatus>(
            Metadata(ControlCommandNames.ResetTestModMessages, AutomationCommandDomain.Network, true),
            (_, _) => ResetTestModMessages());
        router.Register<ClickWidgetCommand, ControlAck>(
            Metadata(
                ControlCommandNames.ClickWidget,
                AutomationCommandDomain.Ui,
                true,
                stability: AutomationCommandStability.Legacy),
            (_, command) => QueueTypedWidgetClick(command));
        router.Register<ExecuteLegacyScriptCommand, object?>(
            Metadata(
                ControlCommandNames.ExecuteLegacyScript,
                AutomationCommandDomain.Legacy,
                true,
                stability: AutomationCommandStability.Legacy),
            (_, command) => ExecuteTypedLegacyScript(command));

        return router;
    }

    private static AutomationCommandDescriptor Metadata(
        string name,
        AutomationCommandDomain domain,
        bool mutatesState = false,
        string[]? allowedRoles = null,
        bool requiresProject = false,
        bool requiresNetwork = false,
        AutomationCommandStability stability = AutomationCommandStability.Stable) =>
        new(
            name,
            domain,
            stability,
            mutatesState,
            allowedRoles ?? ["*"],
            requiresProject,
            requiresNetwork);

    private static AutomationDescription BuildAutomationDescription(TypedCommandRouter router) {
        string assemblyIdentity = typeof(TestAutomationModLoader).Assembly.GetName().FullName
            ?? typeof(TestAutomationModLoader).Assembly.GetName().Name
            ?? "Survivalcraft.TestAutomation";
        AutomationCommandDescriptor[] commands = router.Commands.ToArray();
        return new AutomationDescription(
            ControlProtocol.CurrentVersion,
            AutomationProtocol.CurrentApiVersion,
            [1, AutomationProtocol.CurrentSnapshotSchemaVersion],
            commands,
            commands
                .Select(command => command.Domain.ToString())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(domain => domain, StringComparer.Ordinal)
                .ToArray(),
            new AutomationLimits(
                ControlProtocol.MaximumFrameBytes,
                AutomationProtocol.MaximumSnapshotEntities,
                AutomationProtocol.MaximumSnapshotTerrainCells,
                AutomationProtocol.MaximumSnapshotInventorySlots,
                AutomationProtocol.MaximumSnapshotLogicElements,
                AutomationProtocol.MaximumEventQueryCount,
                AutomationProtocol.DefaultEventJournalCapacity),
            assemblyIdentity,
            LegacyExecutionEnabled: true);
    }

    private StateSnapshotV2 BuildSnapshotV2(GetSnapshotV2Command command) {
        SnapshotDomain[] requestedDomains = command.Domains
            ?? throw new AutomationCommandException(
                "invalid_payload",
                "Snapshot domains are required.");
        if (requestedDomains.Any(domain => !Enum.IsDefined(domain))) {
            throw new AutomationCommandException(
                "invalid_payload",
                "Snapshot contains an unknown domain.");
        }
        HashSet<SnapshotDomain> domains = requestedDomains.ToHashSet();
        if (command.PlayerIndexes is { Length: > 64 }) {
            throw new AutomationCommandException(
                "limit_exceeded",
                "At most 64 player indexes can be selected.");
        }
        if (command.TerrainCells is { Length: > AutomationProtocol.MaximumSnapshotTerrainCells }) {
            throw new AutomationCommandException(
                "limit_exceeded",
                $"At most {AutomationProtocol.MaximumSnapshotTerrainCells} terrain cells can be selected.");
        }
        if (command.TerrainCells?.Any(cell => cell.Y is < 0 or > 255) == true) {
            throw new AutomationCommandException(
                "invalid_payload",
                "Terrain cell Y coordinates must be between 0 and 255.");
        }

        Project? project = GameManager.Project;
        SubsystemPlayers? subsystemPlayers = project?.FindSubsystem<SubsystemPlayers>();
        HashSet<int>? selectedPlayerIndexes = command.PlayerIndexes?.ToHashSet();
        PlayerSnapshotV2[] players = domains.Contains(SnapshotDomain.Players)
            ? subsystemPlayers?.PlayersData
                .Where(player => selectedPlayerIndexes == null
                    || selectedPlayerIndexes.Contains(player.PlayerIndex))
                .Select(BuildPlayerSnapshotV2)
                .OrderBy(player => player.PlayerIndex)
                .ToArray() ?? []
            : [];

        NetworkSnapshotV2? network = domains.Contains(SnapshotDomain.Network)
            ? BuildNetworkSnapshotV2()
            : null;
        SubsystemTerrain? subsystemTerrain = project?.FindSubsystem<SubsystemTerrain>();
        TerrainCellSnapshotV2[] terrainCells = domains.Contains(SnapshotDomain.Terrain)
            && subsystemTerrain != null
            ? (command.TerrainCells ?? [])
                .Distinct()
                .Select(cell => {
                    TerrainChunk? chunk = subsystemTerrain.Terrain.GetChunkAtCoords(
                        cell.X >> 4,
                        cell.Z >> 4);
                    bool isLoaded = chunk != null
                        && chunk.ThreadState > TerrainChunkState.InvalidContents4;
                    int value = isLoaded
                        ? subsystemTerrain.Terrain.GetCellValue(cell.X, cell.Y, cell.Z)
                        : 0;
                    long revision = !isLoaded
                        ? -1
                        : NetworkManager.IsClientRunning
                            ? TerrainRevisionTracker.GetAppliedRevision(
                                subsystemTerrain,
                                cell.X >> 4,
                                cell.Z >> 4)
                            : TerrainRevisionTracker.GetAuthoritativeRevision(
                                subsystemTerrain,
                                cell.X >> 4,
                                cell.Z >> 4);
                    return new TerrainCellSnapshotV2(
                        cell.X,
                        cell.Y,
                        cell.Z,
                        Terrain.ReplaceLight(value, 0),
                        Terrain.ExtractContents(value),
                        Terrain.ExtractData(value),
                        isLoaded,
                        revision,
                        chunk?.ThreadState.ToString() ?? "unallocated");
                })
                .OrderBy(cell => cell.X)
                .ThenBy(cell => cell.Y)
                .ThenBy(cell => cell.Z)
                .ToArray()
            : [];
        TerrainStreamingSnapshotV2? terrainStreaming = domains.Contains(SnapshotDomain.Terrain)
            && subsystemTerrain != null
                ? BuildTerrainStreamingSnapshotV2(subsystemTerrain)
                : null;
        ActionEventRecord[] events = domains.Contains(SnapshotDomain.Events)
            ? actionEventJournal.Query(new GetEventsCommand(
                command.EventAfterSequence,
                command.EventLimit)).Events
            : [];
        Dictionary<string, string> diagnostics = new(StringComparer.Ordinal);
        if (command.IncludeVolatileDiagnostics) {
            diagnostics["processId"] = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
            diagnostics["responseSequence"] = typedResponseSequence.ToString(CultureInfo.InvariantCulture);
        }

        return new StateSnapshotV2(
            AutomationProtocol.CurrentSnapshotSchemaVersion,
            Guid.NewGuid().ToString("N"),
            Time.FrameIndex,
            Time.RealTime,
            new NodeSnapshotV2(
                nodeRole,
                ScreensManager.CurrentScreen?.GetType().Name ?? "null",
                LoadingScreen.m_isContentLoaded,
                project != null,
                project == null ? "none" : GameManager.IsNetworkProject ? "network" : "storage"),
            network,
            players,
            terrainCells,
            events,
            diagnostics,
            terrainStreaming);
    }

    private static TerrainStreamingSnapshotV2 BuildTerrainStreamingSnapshotV2(
        SubsystemTerrain terrain) {
        TerrainUpdater updater = terrain.TerrainUpdater;
        TerrainRevisionMetrics revisions = TerrainRevisionTracker.GetMetrics(terrain);
        TerrainUpdateLocationSnapshotV2[] locations = updater.UpdateLocations
            .OrderBy(item => item.Key)
            .Select(item => new TerrainUpdateLocationSnapshotV2(
                item.Key,
                ToVector(item.Value.Center),
                item.Value.VisibilityDistance,
                item.Value.ContentDistance,
                updater.waitChunkList.TryGetValue(item.Key, out Queue<Point2>? pending)
                    ? pending.Count
                    : 0,
                updater.sentChunkSetList.TryGetValue(item.Key, out HashSet<Point2>? sent)
                    ? sent.Count
                    : 0))
            .ToArray();
        TerrainChunk[] chunks = terrain.Terrain.AllocatedChunks;
        return new TerrainStreamingSnapshotV2(
            locations,
            chunks.Length,
            chunks.Count(chunk => chunk.ThreadState > TerrainChunkState.InvalidContents4),
            updater.waitChunkList.Values.Sum(queue => queue.Count),
            updater.sentChunkSetList.Values.Sum(set => set.Count),
            revisions.AuthoritativeTrackedChunks,
            revisions.ClientTrackedChunks,
            revisions.AppliedSnapshots,
            revisions.AppliedChanges,
            revisions.IgnoredStalePackets,
            revisions.IgnoredUnloadedChanges,
            revisions.BufferedChanges,
            revisions.BufferOverflows,
            revisions.PendingChanges);
    }

    private static NetworkSnapshotV2 BuildNetworkSnapshotV2() {
        string mode = NetworkManager.IsServerRunning && NetworkManager.IsClientRunning
            ? "server+client"
            : NetworkManager.IsServerRunning
                ? "server"
                : NetworkManager.IsClientRunning ? "client" : "none";
        RelayStatus relay = BuildRelayStatus();
        NetworkFaultMetrics metrics = NetworkFaultInjector.Metrics;
        return new NetworkSnapshotV2(
            mode,
            NetworkManager.MyState.ToString(),
            NetworkManager.PlayerIndex,
            NetworkManager.ServerSessions.Count(),
            relay.Mode,
            metrics.Sent,
            metrics.Dropped,
            metrics.Duplicated,
            metrics.Pending);
    }

    private static PlayerSnapshotV2 BuildPlayerSnapshotV2(PlayerData player) {
        ComponentPlayer? componentPlayer = player.ComponentPlayer;
        ComponentBody? body = componentPlayer?.ComponentBody;
        PlayerBodySnapshotV2? bodyState = body == null
            ? null
            : new PlayerBodySnapshotV2(
                ToVector(body.Position),
                ToVector(body.Velocity),
                body.IsCrouching,
                ToVector(componentPlayer!.ComponentLocomotion.LookAngles),
                ToQuaternion(body.Rotation));
        return new PlayerSnapshotV2(
            player.PlayerIndex,
            player.Name ?? string.Empty,
            ResolveNetworkIdentity(player),
            player.IsMainPlayer,
            player.IsReadyForPlaying,
            componentPlayer?.Entity.Id,
            bodyState,
            componentPlayer?.ComponentHealth?.Health,
            player.Level,
            componentPlayer?.ComponentLocomotion?.IsCreativeFlyEnabled == true);
    }

    private static Vector2State ToVector(Vector2 vector) =>
        new(vector.X, vector.Y);

    private static QuaternionState ToQuaternion(Quaternion value) =>
        new(value.X, value.Y, value.Z, value.W);

    private static string? ResolveNetworkIdentity(PlayerData player) {
        if (player.IsMainPlayer && NetworkManager.NetworkGuid != Guid.Empty) {
            return NetworkManager.NetworkGuid.ToString("D");
        }
        ClientSession? session = NetworkManager.ServerSessions.FirstOrDefault(candidate =>
            candidate.PlayerIndex == player.PlayerIndex);
        return session?.NetworkGuid != Guid.Empty
            ? session?.NetworkGuid.ToString("D")
            : null;
    }

    private ActionReceipt SetCreativeFly(
        AutomationCommandContext context,
        SetCreativeFlyCommand command) {
        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        if (!NetworkManager.IsClientRunning) {
            throw new AutomationCommandException(
                "permission_denied",
                "Creative-fly gameplay actions must originate from a connected client node.");
        }
        SubsystemPlayers players = project.FindSubsystem<SubsystemPlayers>(true);
        PlayerData? actor = command.PlayerIndex.HasValue
            ? players.PlayersData.FirstOrDefault(player => player.PlayerIndex == command.PlayerIndex.Value)
            : players.MainPlayerData;
        if (actor?.ComponentPlayer == null) {
            throw new AutomationCommandException("actor_not_found", "The requested player is not ready.");
        }
        if (!actor.IsMainPlayer) {
            throw new AutomationCommandException(
                "permission_denied",
                "A client can only drive its main player.");
        }

        long tick = Time.FrameIndex;
        bool previous = actor.ComponentPlayer.ComponentLocomotion.IsCreativeFlyEnabled;
        actionEventJournal.Record(
            tick,
            context.RequestId,
            nodeRole,
            AutomationCommandDomain.Player,
            "creativeFly",
            ActionEventPhase.Queued,
            actor.PlayerIndex,
            code: "queued",
            data: new Dictionary<string, string> {
                ["enabled"] = command.Enabled.ToString(CultureInfo.InvariantCulture)
            });

        if (previous == command.Enabled) {
            actionEventJournal.Record(
                tick,
                context.RequestId,
                nodeRole,
                AutomationCommandDomain.Player,
                "creativeFly",
                ActionEventPhase.Observed,
                actor.PlayerIndex,
                code: "already_applied");
            return new ActionReceipt(
                context.RequestId,
                true,
                tick,
                actor.PlayerIndex,
                "already_applied");
        }

        bool accepted = actor.ComponentPlayer.ComponentGui.TrySetCreativeFlyEnabled(command.Enabled);
        string code = accepted ? "applied" : "action_rejected";
        actionEventJournal.Record(
            tick,
            context.RequestId,
            nodeRole,
            AutomationCommandDomain.Player,
            "creativeFly",
            accepted ? ActionEventPhase.Applied : ActionEventPhase.Rejected,
            actor.PlayerIndex,
            code: code,
            data: new Dictionary<string, string> {
                ["enabled"] = command.Enabled.ToString(CultureInfo.InvariantCulture)
            });
        return new ActionReceipt(
            context.RequestId,
            accepted,
            tick,
            actor.PlayerIndex,
            code,
            accepted ? null : "The production gameplay path rejected the requested fly state.");
    }
}
