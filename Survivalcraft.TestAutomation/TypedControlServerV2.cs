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
        router.Register<PrepareEntityLifecycleFixtureCommand, EntityLifecycleFixture>(
            Metadata(
                ControlCommandNames.PrepareEntityLifecycleFixture,
                AutomationCommandDomain.Fixture,
                true,
                allowedRoles: ["server"],
                requiresProject: true,
                requiresNetwork: true,
                stability: AutomationCommandStability.Experimental),
            (_, command) => PrepareEntityLifecycleFixture(command));
        router.Register<ApplyEntityImpulseCommand, ActionReceipt>(
            Metadata(
                ControlCommandNames.ApplyEntityImpulse,
                AutomationCommandDomain.Entity,
                true,
                allowedRoles: ["server"],
                requiresProject: true,
                requiresNetwork: true,
                stability: AutomationCommandStability.Experimental),
            ApplyEntityImpulse);
        router.Register<RunEntityLifecycleBurstCommand, EntityLifecycleBurstResult>(
            Metadata(
                ControlCommandNames.RunEntityLifecycleBurst,
                AutomationCommandDomain.Entity,
                true,
                allowedRoles: ["server"],
                requiresProject: true,
                requiresNetwork: true,
                stability: AutomationCommandStability.Experimental),
            RunEntityLifecycleBurst);
        router.Register<PrepareInventoryFixtureCommand, InventoryFixture>(
            Metadata(
                ControlCommandNames.PrepareInventoryFixture,
                AutomationCommandDomain.Fixture,
                true,
                allowedRoles: ["server"],
                requiresProject: true,
                requiresNetwork: true,
                stability: AutomationCommandStability.Experimental),
            (_, command) => PrepareInventoryFixture(command));
        router.Register<ApplyInventoryFixtureCommand, ControlAck>(
            Metadata(
                ControlCommandNames.ApplyInventoryFixture,
                AutomationCommandDomain.Fixture,
                true,
                allowedRoles: ["client", "client-a", "client-b", "client-c"],
                requiresProject: true,
                requiresNetwork: true,
                stability: AutomationCommandStability.Experimental),
            (_, command) => ApplyInventoryFixture(command));
        router.Register<InventoryMoveCommand, ActionReceipt>(
            Metadata(
                ControlCommandNames.InventoryMove,
                AutomationCommandDomain.Inventory,
                true,
                allowedRoles: ["client", "client-a", "client-b", "client-c"],
                requiresProject: true,
                requiresNetwork: true,
                stability: AutomationCommandStability.Experimental),
            QueueInventoryMove);
        router.Register<InventorySelectSlotCommand, ActionReceipt>(
            Metadata(
                ControlCommandNames.InventorySelectSlot,
                AutomationCommandDomain.Inventory,
                true,
                allowedRoles: ["client", "client-a", "client-b", "client-c"],
                requiresProject: true,
                requiresNetwork: true,
                stability: AutomationCommandStability.Experimental),
            QueueInventorySelectSlot);
        router.Register<InventoryDropCommand, ActionReceipt>(
            Metadata(
                ControlCommandNames.InventoryDrop,
                AutomationCommandDomain.Inventory,
                true,
                allowedRoles: ["client", "client-a", "client-b", "client-c"],
                requiresProject: true,
                requiresNetwork: true,
                stability: AutomationCommandStability.Experimental),
            QueueInventoryDrop);
        router.Register<RunInventoryRevisionBurstCommand, InventoryRevisionBurstResult>(
            Metadata(
                ControlCommandNames.RunInventoryRevisionBurst,
                AutomationCommandDomain.Inventory,
                true,
                allowedRoles: ["server"],
                requiresProject: true,
                requiresNetwork: true,
                stability: AutomationCommandStability.Experimental),
            RunInventoryRevisionBurst);
        router.Register<PrepareHealthFixtureCommand, HealthFixture>(
            Metadata(
                ControlCommandNames.PrepareHealthFixture,
                AutomationCommandDomain.Fixture,
                true,
                allowedRoles: ["server"],
                requiresProject: true,
                requiresNetwork: true,
                stability: AutomationCommandStability.Experimental),
            (_, command) => PrepareHealthFixture(command));
        router.Register<SetHealthFixtureCommand, SetHealthFixtureResult>(
            Metadata(
                ControlCommandNames.SetHealthFixture,
                AutomationCommandDomain.Fixture,
                true,
                allowedRoles: ["server"],
                requiresProject: true,
                requiresNetwork: true,
                stability: AutomationCommandStability.Experimental),
            (_, command) => SetHealthFixture(command));
        router.Register<ApplyHealthFixtureCommand, ControlAck>(
            Metadata(
                ControlCommandNames.ApplyHealthFixture,
                AutomationCommandDomain.Fixture,
                true,
                allowedRoles: ["client", "client-a", "client-b", "client-c"],
                requiresProject: true,
                requiresNetwork: true,
                stability: AutomationCommandStability.Experimental),
            (_, command) => ApplyHealthFixture(command));
        router.Register<PlayerHitCommand, ActionReceipt>(
            Metadata(
                ControlCommandNames.PlayerHit,
                AutomationCommandDomain.Health,
                true,
                allowedRoles: ["client", "client-a", "client-b", "client-c"],
                requiresProject: true,
                requiresNetwork: true,
                stability: AutomationCommandStability.Experimental),
            QueuePlayerHit);
        router.Register<PlayerRequestRespawnCommand, ActionReceipt>(
            Metadata(
                ControlCommandNames.PlayerRequestRespawn,
                AutomationCommandDomain.Health,
                true,
                allowedRoles: ["client", "client-a", "client-b", "client-c"],
                requiresProject: true,
                requiresNetwork: true,
                stability: AutomationCommandStability.Experimental),
            QueuePlayerRespawn);
        router.Register<RunHealthRevisionBurstCommand, HealthRevisionBurstResult>(
            Metadata(
                ControlCommandNames.RunHealthRevisionBurst,
                AutomationCommandDomain.Health,
                true,
                allowedRoles: ["server"],
                requiresProject: true,
                requiresNetwork: true,
                stability: AutomationCommandStability.Experimental),
            RunHealthRevisionBurst);
        router.Register<SetEnvironmentFixtureCommand, SetEnvironmentFixtureResult>(
            Metadata(
                ControlCommandNames.SetEnvironmentFixture,
                AutomationCommandDomain.Fixture,
                true,
                allowedRoles: ["server"],
                requiresProject: true,
                requiresNetwork: true,
                stability: AutomationCommandStability.Experimental),
            (_, command) => SetEnvironmentFixture(command));
        router.Register<EnvironmentRequestChangeCommand, ActionReceipt>(
            Metadata(
                ControlCommandNames.EnvironmentRequestChange,
                AutomationCommandDomain.Environment,
                true,
                allowedRoles: ["client", "client-a", "client-b", "client-c"],
                requiresProject: true,
                requiresNetwork: true,
                stability: AutomationCommandStability.Experimental),
            QueueEnvironmentChange);
        router.Register<RunEnvironmentRevisionBurstCommand, EnvironmentRevisionBurstResult>(
            Metadata(
                ControlCommandNames.RunEnvironmentRevisionBurst,
                AutomationCommandDomain.Environment,
                true,
                allowedRoles: ["server"],
                requiresProject: true,
                requiresNetwork: true,
                stability: AutomationCommandStability.Experimental),
            RunEnvironmentRevisionBurst);
        router.Register<PrepareMechanicsFixtureCommand, MechanicsFixture>(
            Metadata(
                ControlCommandNames.PrepareMechanicsFixture,
                AutomationCommandDomain.Fixture,
                true,
                allowedRoles: ["server"],
                requiresProject: true,
                requiresNetwork: true,
                stability: AutomationCommandStability.Experimental),
            (_, command) => PrepareMechanicsFixture(command));
        router.Register<SetPlayerFireFixtureCommand, SetPlayerFireFixtureResult>(
            Metadata(
                ControlCommandNames.SetPlayerFireFixture,
                AutomationCommandDomain.Fixture,
                true,
                allowedRoles: ["server"],
                requiresProject: true,
                requiresNetwork: true,
                stability: AutomationCommandStability.Experimental),
            (_, command) => SetPlayerFireFixture(command));
        router.Register<PlayerSleepCommand, ActionReceipt>(
            Metadata(
                ControlCommandNames.PlayerSleep,
                AutomationCommandDomain.Mechanics,
                true,
                allowedRoles: ["client", "client-a", "client-b", "client-c"],
                requiresProject: true,
                requiresNetwork: true,
                stability: AutomationCommandStability.Experimental),
            QueuePlayerSleep);
        router.Register<PlayerIgniteCommand, ActionReceipt>(
            Metadata(
                ControlCommandNames.PlayerIgnite,
                AutomationCommandDomain.Mechanics,
                true,
                allowedRoles: ["client", "client-a", "client-b", "client-c"],
                requiresProject: true,
                requiresNetwork: true,
                stability: AutomationCommandStability.Experimental),
            QueuePlayerIgnite);
        router.Register<RunMechanicsRevisionBurstCommand, MechanicsRevisionBurstResult>(
            Metadata(
                ControlCommandNames.RunMechanicsRevisionBurst,
                AutomationCommandDomain.Mechanics,
                true,
                allowedRoles: ["server"],
                requiresProject: true,
                requiresNetwork: true,
                stability: AutomationCommandStability.Experimental),
            RunMechanicsRevisionBurst);
        router.Register<PrepareMountFixtureCommand, MountFixture>(
            Metadata(
                ControlCommandNames.PrepareMountFixture,
                AutomationCommandDomain.Fixture,
                true,
                allowedRoles: ["server"],
                requiresProject: true,
                requiresNetwork: true,
                stability: AutomationCommandStability.Experimental),
            (_, command) => PrepareMountFixture(command));
        router.Register<PlayerMountCommand, ActionReceipt>(
            Metadata(
                ControlCommandNames.PlayerMount,
                AutomationCommandDomain.Mechanics,
                true,
                allowedRoles: ["client", "client-a", "client-b", "client-c"],
                requiresProject: true,
                requiresNetwork: true,
                stability: AutomationCommandStability.Experimental),
            QueuePlayerMount);
        router.Register<RunMountRevisionBurstCommand, MountRevisionBurstResult>(
            Metadata(
                ControlCommandNames.RunMountRevisionBurst,
                AutomationCommandDomain.Mechanics,
                true,
                allowedRoles: ["server"],
                requiresProject: true,
                requiresNetwork: true,
                stability: AutomationCommandStability.Experimental),
            RunMountRevisionBurst);
        router.Register<RunMountLifecycleBurstCommand, MountLifecycleBurstResult>(
            Metadata(
                ControlCommandNames.RunMountLifecycleBurst,
                AutomationCommandDomain.Mechanics,
                true,
                allowedRoles: ["server"],
                requiresProject: true,
                requiresNetwork: true,
                stability: AutomationCommandStability.Experimental),
            RunMountLifecycleBurst);
        router.Register<PrepareLogicFixtureCommand, LogicFixture>(
            Metadata(
                ControlCommandNames.PrepareLogicFixture,
                AutomationCommandDomain.Fixture,
                true,
                allowedRoles: ["server"],
                requiresProject: true,
                requiresNetwork: true,
                stability: AutomationCommandStability.Experimental),
            (_, command) => PrepareLogicFixture(command));
        router.Register<ApplyLogicFixtureCommand, ControlAck>(
            Metadata(
                ControlCommandNames.ApplyLogicFixture,
                AutomationCommandDomain.Fixture,
                true,
                allowedRoles: ["client", "client-a", "client-b", "client-c"],
                requiresProject: true,
                requiresNetwork: true,
                stability: AutomationCommandStability.Experimental),
            (_, command) => ApplyLogicFixture(command));
        router.Register<LogicEditCommand, ActionReceipt>(
            Metadata(
                ControlCommandNames.LogicEdit,
                AutomationCommandDomain.Logic,
                true,
                allowedRoles: ["client", "client-a", "client-b", "client-c"],
                requiresProject: true,
                requiresNetwork: true,
                stability: AutomationCommandStability.Experimental),
            QueueLogicEdit);
        router.Register<LogicInteractCommand, ActionReceipt>(
            Metadata(
                ControlCommandNames.LogicInteract,
                AutomationCommandDomain.Logic,
                true,
                allowedRoles: ["client", "client-a", "client-b", "client-c"],
                requiresProject: true,
                requiresNetwork: true,
                stability: AutomationCommandStability.Experimental),
            QueueLogicInteract);
        router.Register<RunLogicRevisionBurstCommand, LogicRevisionBurstResult>(
            Metadata(
                ControlCommandNames.RunLogicRevisionBurst,
                AutomationCommandDomain.Logic,
                true,
                allowedRoles: ["server"],
                requiresProject: true,
                requiresNetwork: true,
                stability: AutomationCommandStability.Experimental),
            RunLogicRevisionBurst);
        router.Register<RunMovingBlockLifecycleBurstCommand, MovingBlockLifecycleBurstResult>(
            Metadata(
                ControlCommandNames.RunMovingBlockLifecycleBurst,
                AutomationCommandDomain.Logic,
                true,
                allowedRoles: ["server"],
                requiresProject: true,
                requiresNetwork: true,
                stability: AutomationCommandStability.Experimental),
            RunMovingBlockLifecycleBurst);
        router.Register<RunPistonLifecycleCommand, PistonLifecycleResult>(
            Metadata(
                ControlCommandNames.RunPistonLifecycle,
                AutomationCommandDomain.Logic,
                true,
                allowedRoles: ["server"],
                requiresProject: true,
                requiresNetwork: true,
                stability: AutomationCommandStability.Experimental),
            RunPistonLifecycle);
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
        if (command.EntityIds is { Length: > AutomationProtocol.MaximumSnapshotEntities }) {
            throw new AutomationCommandException(
                "limit_exceeded",
                $"At most {AutomationProtocol.MaximumSnapshotEntities} entity IDs can be selected.");
        }
        if (command.EntityIds?.Any(entityId => entityId <= 0) == true) {
            throw new AutomationCommandException(
                "invalid_payload",
                "Entity IDs must be positive.");
        }
        if (command.InventoryIds is { Length: > 256 }) {
            throw new AutomationCommandException(
                "limit_exceeded",
                "At most 256 inventory IDs can be selected.");
        }
        if (command.InventoryIds?.Any(inventoryId => inventoryId <= 0) == true) {
            throw new AutomationCommandException(
                "invalid_payload",
                "Inventory IDs must be positive.");
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
        if (command.LogicCells is { Length: > AutomationProtocol.MaximumSnapshotLogicElements }) {
            throw new AutomationCommandException(
                "limit_exceeded",
                $"At most {AutomationProtocol.MaximumSnapshotLogicElements} logic cells can be selected.");
        }
        if (command.LogicCells?.Any(cell => cell.Y is < 0 or > 255) == true) {
            throw new AutomationCommandException(
                "invalid_payload",
                "Logic cell Y coordinates must be between 0 and 255.");
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
        EntitySnapshotV2[]? entities = domains.Contains(SnapshotDomain.Entities)
            && project != null
                ? (command.EntityIds ?? project.Entities
                        .Select(entity => entity.Id)
                        .Where(entityId => entityId > 0)
                        .Distinct()
                        .OrderBy(entityId => entityId)
                        .Take(AutomationProtocol.MaximumSnapshotEntities)
                        .ToArray())
                    .Distinct()
                    .Select(entityId => BuildEntitySnapshotV2(project, entityId))
                    .OrderBy(entity => entity.EntityId)
                    .ToArray()
                : null;
        EntityLifecycleSnapshotV2? entityLifecycle = domains.Contains(SnapshotDomain.Entities)
            && project != null
                ? BuildEntityLifecycleSnapshotV2(project)
                : null;
        InventorySnapshotV2[]? inventories = domains.Contains(SnapshotDomain.Inventories)
            && project != null
                ? BuildInventorySnapshotsV2(project, command.InventoryIds)
                : null;
        HealthStateSnapshotV2[]? healthStates = domains.Contains(SnapshotDomain.Health)
            && project != null
                ? BuildHealthStateSnapshotsV2(project, command.EntityIds)
                : null;
        EnvironmentSnapshotV2? environment = domains.Contains(SnapshotDomain.Environment)
            && project != null
                ? BuildEnvironmentSnapshotV2(project)
                : null;
        MechanicsStateSnapshotV2[]? mechanicsStates = domains.Contains(SnapshotDomain.Mechanics)
            && project != null
                ? BuildMechanicsStateSnapshotsV2(project, command.EntityIds)
                : null;
        LogicElementStateSnapshotV2[]? logicStates = domains.Contains(SnapshotDomain.Logic)
            && project != null
                ? BuildLogicElementSnapshotsV2(
                    project,
                    command.LogicCells ?? command.TerrainCells ?? [])
                : null;
        PersistentVoltageSnapshotV2[]? persistentVoltages = domains.Contains(SnapshotDomain.Logic)
            && project != null
                ? BuildPersistentVoltageSnapshotsV2(project)
                : null;
        MovingBlockStateSnapshotV2[]? movingBlocks = domains.Contains(SnapshotDomain.Logic)
            && project != null
                ? BuildMovingBlockSnapshotsV2(project)
                : null;
        LogicRevisionMetricsSnapshotV2? logicRevisions = domains.Contains(SnapshotDomain.Logic)
            && project != null
                ? BuildLogicRevisionMetricsSnapshotV2(project)
                : null;
        StateRevisionMetricsSnapshotV2? stateRevisions =
            (domains.Contains(SnapshotDomain.Inventories)
                || domains.Contains(SnapshotDomain.Health)
                || domains.Contains(SnapshotDomain.Environment)
                || domains.Contains(SnapshotDomain.Mechanics))
            && project != null
                ? BuildStateRevisionMetricsSnapshotV2(project)
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
            terrainStreaming,
            entities,
            entityLifecycle,
            inventories,
            healthStates,
            stateRevisions,
            environment,
            mechanicsStates,
            logicStates,
            persistentVoltages,
            movingBlocks,
            logicRevisions);
    }

    private static LogicElementStateSnapshotV2[] BuildLogicElementSnapshotsV2(
        Project project,
        TerrainCellCoordinate[] selectedCells) {
        SubsystemTerrain terrain = project.FindSubsystem<SubsystemTerrain>(true);
        SubsystemMemoryBankBlockBehavior memory =
            project.FindSubsystem<SubsystemMemoryBankBlockBehavior>(true);
        SubsystemTruthTableCircuitBlockBehavior truth =
            project.FindSubsystem<SubsystemTruthTableCircuitBlockBehavior>(true);
        SubsystemSignBlockBehavior signs =
            project.FindSubsystem<SubsystemSignBlockBehavior>(true);
        bool client = NetworkManager.IsClientRunning;
        return selectedCells
            .Distinct()
            .Take(AutomationProtocol.MaximumSnapshotLogicElements)
            .Select(cell => {
                Point3 point = new(cell.X, cell.Y, cell.Z);
                int value = Terrain.ReplaceLight(
                    terrain.Terrain.GetCellValue(cell.X, cell.Y, cell.Z),
                    0);
                int contents = Terrain.ExtractContents(value);
                int data = Terrain.ExtractData(value);
                (NetworkLogicElementKind ElementKind, string Kind, string State) descriptor =
                    DescribeLogicElement(contents, data, point, memory, truth, signs);
                long revision = descriptor.ElementKind == NetworkLogicElementKind.None
                    ? -1
                    : client
                        ? NetworkLogicStateTracker.GetAppliedRevision(
                            project,
                            NetworkLogicStateKey.Editable(
                                descriptor.ElementKind,
                                false,
                                0,
                                0,
                                point))
                        : NetworkLogicStateTracker.GetAuthoritativeRevision(
                            project,
                            NetworkLogicStateKey.Editable(
                                descriptor.ElementKind,
                                false,
                                0,
                                0,
                                point));
                return new LogicElementStateSnapshotV2(
                    cell.X,
                    cell.Y,
                    cell.Z,
                    descriptor.Kind,
                    value,
                    contents,
                    data,
                    descriptor.State,
                    revision);
            })
            .OrderBy(state => state.X)
            .ThenBy(state => state.Y)
            .ThenBy(state => state.Z)
            .ToArray();
    }

    private static (
        NetworkLogicElementKind ElementKind,
        string Kind,
        string State) DescribeLogicElement(
        int contents,
        int data,
        Point3 point,
        SubsystemMemoryBankBlockBehavior memory,
        SubsystemTruthTableCircuitBlockBehavior truth,
        SubsystemSignBlockBehavior signs) {
        if (contents == AdjustableDelayGateBlock.Index) {
            return (
                NetworkLogicElementKind.AdjustableDelayGate,
                "adjustableDelayGate",
                $"delay={AdjustableDelayGateBlock.GetDelay(data)}");
        }
        if (contents == BatteryBlock.Index) {
            return (
                NetworkLogicElementKind.Battery,
                "battery",
                $"voltage={BatteryBlock.GetVoltageLevel(data)}");
        }
        if (contents == ButtonBlock.Index) {
            return (
                NetworkLogicElementKind.Button,
                "button",
                $"voltage={ButtonBlock.GetVoltageLevel(data)}");
        }
        if (contents == SwitchBlock.Index) {
            return (
                NetworkLogicElementKind.Switch,
                "switch",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"voltage={SwitchBlock.GetVoltageLevel(data)};on={SwitchBlock.GetLeverState(Terrain.MakeBlockValue(contents, 0, data))}"));
        }
        if (contents == PistonBlock.Index) {
            return (
                NetworkLogicElementKind.Piston,
                "piston",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"mode={PistonBlock.GetMode(data)};face={PistonBlock.GetFace(data)};extension={PistonBlock.GetMaxExtension(data)};pull={PistonBlock.GetPullCount(data)};speed={PistonBlock.GetSpeed(data)};extended={PistonBlock.GetIsExtended(data)}"));
        }
        if (contents == MemoryBankBlock.Index) {
            MemoryBankData? state = memory.GetBlockData(point);
            return (
                NetworkLogicElementKind.MemoryBank,
                "memoryBank",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"data={state?.SaveString() ?? string.Empty};lastOutput={state?.LastOutput ?? 0}"));
        }
        if (contents == TruthTableCircuitBlock.Index) {
            TruthTableData? state = truth.GetBlockData(point);
            return (
                NetworkLogicElementKind.TruthTable,
                "truthTable",
                $"data={state?.SaveString() ?? string.Empty}");
        }
        if (contents >= 0
            && contents < BlocksManager.Blocks.Length
            && BlocksManager.Blocks[contents] is SignBlock) {
            SignData? state = signs.GetSignData(point);
            string lines = string.Join("|", state?.Lines ?? []);
            string colors = string.Join(
                ",",
                (state?.Colors ?? []).Select(color =>
                    color.PackedValue.ToString("X8", CultureInfo.InvariantCulture)));
            return (
                NetworkLogicElementKind.Sign,
                "sign",
                $"lines={lines};colors={colors};url={state?.Url ?? string.Empty}");
        }
        if (contents == DispenserBlock.Index) {
            return (
                NetworkLogicElementKind.Dispenser,
                "dispenser",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"mode={DispenserBlock.GetMode(data)};acceptsDrops={DispenserBlock.GetAcceptsDrops(data)}"));
        }
        return (NetworkLogicElementKind.None, "unknown", $"data={data}");
    }

    private static PersistentVoltageSnapshotV2[] BuildPersistentVoltageSnapshotsV2(
        Project project) =>
        project.FindSubsystem<SubsystemElectricity>(true)
            .m_persistentElementsVoltages
            .OrderBy(item => item.Key.X)
            .ThenBy(item => item.Key.Y)
            .ThenBy(item => item.Key.Z)
            .Take(AutomationProtocol.MaximumSnapshotLogicElements)
            .Select(item => new PersistentVoltageSnapshotV2(
                item.Key.X,
                item.Key.Y,
                item.Key.Z,
                item.Value))
            .ToArray();

    private static MovingBlockStateSnapshotV2[] BuildMovingBlockSnapshotsV2(
        Project project) {
        bool client = NetworkManager.IsClientRunning;
        return project.FindSubsystem<SubsystemMovingBlocks>(true)
            .m_movingBlockSets
            .OrderBy(set => set.Id, StringComparer.Ordinal)
            .ThenBy(set => set.Tag is Point3 point ? point.X : 0)
            .ThenBy(set => set.Tag is Point3 point ? point.Y : 0)
            .ThenBy(set => set.Tag is Point3 point ? point.Z : 0)
            .ThenBy(NetworkMovingBlockIdentityTracker.GetOrCreate)
            .Take(AutomationProtocol.MaximumSnapshotLogicElements)
            .Select(set => {
                bool hasTag = set.Tag is Point3;
                Point3 tag = hasTag ? (Point3)set.Tag : default;
                long instanceId = NetworkMovingBlockIdentityTracker.GetOrCreate(set);
                NetworkLogicStateKey key = NetworkMovingBlockIdentityTracker.CreateStateKey(
                    set.Id, hasTag, tag, instanceId);
                long generation = client
                    ? NetworkLogicStateTracker.GetAppliedMovingGeneration(project, key)
                    : NetworkLogicStateTracker.GetAuthoritativeMovingGeneration(project, key);
                long revision = client
                    ? NetworkLogicStateTracker.GetAppliedRevision(project, key)
                    : NetworkLogicStateTracker.GetAuthoritativeRevision(project, key);
                string blocks = string.Join(
                    ";",
                    set.Blocks
                        .OrderBy(block => block.Offset.X)
                        .ThenBy(block => block.Offset.Y)
                        .ThenBy(block => block.Offset.Z)
                        .Select(block => string.Create(
                            CultureInfo.InvariantCulture,
                            $"{block.Offset.X},{block.Offset.Y},{block.Offset.Z}:{Terrain.ReplaceLight(block.Value, 0)}")));
                return new MovingBlockStateSnapshotV2(
                    set.Id,
                    hasTag,
                    hasTag ? new TerrainCellCoordinate(tag.X, tag.Y, tag.Z) : null,
                    instanceId,
                    true,
                    generation,
                    revision,
                    ToVector(set.Position),
                    ToVector(set.TargetPosition),
                    ToVector(set.CurrentVelocity),
                    set.Blocks.Count,
                    blocks);
            })
            .ToArray();
    }

    private static LogicRevisionMetricsSnapshotV2 BuildLogicRevisionMetricsSnapshotV2(
        Project project) {
        NetworkLogicStateMetrics metrics = NetworkLogicStateTracker.GetMetrics(project);
        NetworkLogicStateKey electricityKey = NetworkLogicStateKey.Electricity();
        long electricityRevision = NetworkManager.IsClientRunning
            ? NetworkLogicStateTracker.GetAppliedRevision(project, electricityKey)
            : NetworkLogicStateTracker.GetAuthoritativeRevision(project, electricityKey);
        return new LogicRevisionMetricsSnapshotV2(
            metrics.AuthoritativeTrackedStates,
            metrics.ClientTrackedStates,
            metrics.AppliedStates,
            metrics.IgnoredStaleStates,
            metrics.IgnoredLifecycleStates,
            metrics.AuthoritativeMovingGenerations,
            metrics.ClientMovingGenerations,
            metrics.MovingTombstones,
            electricityRevision);
    }

    private static EnvironmentSnapshotV2 BuildEnvironmentSnapshotV2(Project project) {
        SubsystemGameInfo info = project.FindSubsystem<SubsystemGameInfo>(true);
        SubsystemWeather weather = project.FindSubsystem<SubsystemWeather>(true);
        SubsystemSky sky = project.FindSubsystem<SubsystemSky>(true);
        bool client = NetworkManager.IsClientRunning;
        long Revision(NetworkStateRevisionDomain domain) => client
            ? NetworkStateRevisionTracker.GetAppliedRevision(
                project,
                domain,
                SubsystemTimePacket.EnvironmentStateId)
            : NetworkStateRevisionTracker.GetAuthoritativeRevision(
                project,
                domain,
                SubsystemTimePacket.EnvironmentStateId);
        return new EnvironmentSnapshotV2(
            info.TotalElapsedGameTime,
            info.TimeOfDay.TimeOfDayOffset,
            info.WorldSettings.TimeOfYear,
            weather.m_precipitationStartTime,
            SnapshotScheduleEnd(weather.m_precipitationEndTime),
            weather.m_precipitationRampTime,
            weather.m_lightningIntensity,
            weather.IsPrecipitationStarted,
            weather.m_fogStartTime,
            SnapshotScheduleEnd(weather.m_fogEndTime),
            weather.m_fogRampTime,
            weather.FogProgress,
            weather.IsFogStarted,
            sky.m_lastLightningStrikeTime,
            sky.m_lightningStrikePosition.HasValue
                ? ToVector(sky.m_lightningStrikePosition.Value)
                : null,
            Revision(NetworkStateRevisionDomain.Time),
            Revision(NetworkStateRevisionDomain.Weather),
            Revision(NetworkStateRevisionDomain.Sky),
            info.WorldSettings.GameMode.ToString());
    }

    private static double SnapshotScheduleEnd(double value) =>
        double.IsPositiveInfinity(value) ? double.MaxValue : value;

    private static MechanicsStateSnapshotV2[] BuildMechanicsStateSnapshotsV2(
        Project project,
        int[]? selectedEntityIds) {
        int[] entityIds = selectedEntityIds
            ?? project.Entities
                .Where(entity => entity.FindComponent<ComponentSleep>() != null
                    || entity.FindComponent<ComponentOnFire>() != null
                    || entity.FindComponent<ComponentRider>() != null
                    || entity.FindComponent<ComponentMount>() != null)
                .Select(entity => entity.Id)
                .Where(entityId => entityId > 0)
                .Distinct()
                .OrderBy(entityId => entityId)
                .Take(AutomationProtocol.MaximumSnapshotEntities)
                .ToArray();
        bool client = NetworkManager.IsClientRunning;
        return entityIds
            .Distinct()
            .Select(entityId => {
                Entity? entity = project.FindEntity(entityId);
                ComponentPlayer? player = entity?.FindComponent<ComponentPlayer>();
                ComponentSleep? sleep = entity?.FindComponent<ComponentSleep>();
                ComponentOnFire? onFire = entity?.FindComponent<ComponentOnFire>();
                ComponentRider? rider = entity?.FindComponent<ComponentRider>();
                ComponentMount? mount = entity?.FindComponent<ComponentMount>();
                ComponentMount? authoritativeMount = rider?.AuthoritativeMount;
                ComponentRider? authoritativeRider = mount?.Rider;
                if (authoritativeRider?.AuthoritativeMount != mount) {
                    authoritativeRider = null;
                }
                long Revision(NetworkStateRevisionDomain domain, bool present) => !present
                    ? -1
                    : client
                        ? NetworkStateRevisionTracker.GetAppliedRevision(project, domain, entityId)
                        : NetworkStateRevisionTracker.GetAuthoritativeRevision(project, domain, entityId);
                long lifecycleRevision = client
                    ? EntityLifecycleTracker.GetAppliedRevision(project, entityId)
                    : EntityLifecycleTracker.GetAuthoritativeRevision(project, entityId);
                return new MechanicsStateSnapshotV2(
                    entityId,
                    entity != null,
                    player != null,
                    player?.PlayerData?.PlayerIndex,
                    sleep != null,
                    sleep?.IsSleeping == true,
                    sleep?.m_allowManualWakeUp == true,
                    sleep?.m_sleepStartTime,
                    Revision(NetworkStateRevisionDomain.Sleep, sleep != null),
                    onFire != null,
                    onFire?.IsOnFire == true,
                    onFire?.m_fireDuration ?? 0,
                    Revision(NetworkStateRevisionDomain.Fire, onFire != null),
                    lifecycleRevision,
                    rider != null,
                    authoritativeMount?.Entity?.Id,
                    rider?.m_isAnimating == true,
                    rider?.m_isDismounting == true,
                    Revision(NetworkStateRevisionDomain.Mount, rider != null),
                    authoritativeMount?.Entity?.Id is int mountEntityId
                        ? client
                            ? EntityLifecycleTracker.GetAppliedRevision(project, mountEntityId)
                            : EntityLifecycleTracker.GetAuthoritativeRevision(project, mountEntityId)
                        : -1,
                    mount != null,
                    authoritativeRider?.Entity?.Id);
            })
            .OrderBy(state => state.EntityId)
            .ToArray();
    }

    private static InventorySnapshotV2[] BuildInventorySnapshotsV2(
        Project project,
        int[]? selectedInventoryIds) {
        HashSet<int>? selected = selectedInventoryIds?.ToHashSet();
        IInventory[] inventories = project.Entities
            .SelectMany(entity => entity.FindComponents<IInventory>())
            .Where(inventory => inventory.Id > 0
                && (selected == null || selected.Contains(inventory.Id)))
            .GroupBy(inventory => inventory.Id)
            .Select(group => group.First())
            .OrderBy(inventory => inventory.Id)
            .ToArray();
        int totalSlots = inventories.Sum(inventory => inventory.SlotsCount);
        if (totalSlots > AutomationProtocol.MaximumSnapshotInventorySlots) {
            throw new AutomationCommandException(
                "limit_exceeded",
                $"Selected inventories contain {totalSlots} slots; maximum is "
                + $"{AutomationProtocol.MaximumSnapshotInventorySlots}.");
        }
        return inventories.Select(BuildInventorySnapshotV2).ToArray();
    }

    private static InventorySnapshotV2 BuildInventorySnapshotV2(IInventory inventory) {
        Component? component = inventory as Component;
        Entity? entity = component?.Entity;
        ComponentBlockEntity? blockEntity = entity?.FindComponent<ComponentBlockEntity>();
        string kind = inventory switch {
            ComponentChest => "chest",
            ComponentFurnace => "furnace",
            ComponentDispenser => "dispenser",
            ComponentCraftingTable => "craftingTable",
            ComponentClothing => "clothing",
            ComponentCreativeInventory => "creative",
            ComponentInventory _ when entity?.FindComponent<ComponentPlayer>() != null => "player",
            _ => "inventory"
        };
        long revision = NetworkManager.IsClientRunning
            ? NetworkStateRevisionTracker.GetAppliedRevision(
                inventory.Project,
                NetworkStateRevisionDomain.Inventory,
                inventory.Id)
            : NetworkStateRevisionTracker.GetAuthoritativeRevision(
                inventory.Project,
                NetworkStateRevisionDomain.Inventory,
                inventory.Id);
        InventorySlotSnapshotV2[] slots = Enumerable.Range(0, inventory.SlotsCount)
            .Select(index => {
                int value = inventory.GetSlotValue(index);
                return new InventorySlotSnapshotV2(
                    index,
                    value,
                    inventory.GetSlotCount(index),
                    Terrain.ExtractContents(value),
                    Terrain.ExtractData(value));
            })
            .ToArray();
        return new InventorySnapshotV2(
            inventory.Id,
            entity?.Id > 0 ? entity.Id : null,
            inventory.GetType().FullName ?? inventory.GetType().Name,
            kind,
            inventory.SlotsCount,
            inventory.ActiveSlotIndex,
            revision,
            slots,
            blockEntity == null
                ? null
                : new TerrainCellCoordinate(
                    blockEntity.Coordinates.X,
                    blockEntity.Coordinates.Y,
                    blockEntity.Coordinates.Z));
    }

    private static HealthStateSnapshotV2[] BuildHealthStateSnapshotsV2(
        Project project,
        int[]? selectedEntityIds) {
        int[] entityIds = selectedEntityIds
            ?? project.Entities
                .Where(entity => entity.FindComponent<ComponentHealth>() != null
                    || entity.FindComponent<ComponentDamage>() != null)
                .Select(entity => entity.Id)
                .Where(entityId => entityId > 0)
                .Distinct()
                .OrderBy(entityId => entityId)
                .Take(AutomationProtocol.MaximumSnapshotEntities)
                .ToArray();
        return entityIds
            .Distinct()
            .Select(entityId => BuildHealthStateSnapshotV2(project, entityId))
            .OrderBy(state => state.EntityId)
            .ToArray();
    }

    private static HealthStateSnapshotV2 BuildHealthStateSnapshotV2(
        Project project,
        int entityId) {
        Entity? entity = project.FindEntity(entityId);
        ComponentHealth? health = entity?.FindComponent<ComponentHealth>();
        ComponentDamage? damage = entity?.FindComponent<ComponentDamage>();
        ComponentPlayer? player = entity?.FindComponent<ComponentPlayer>();
        bool client = NetworkManager.IsClientRunning;
        long healthRevision = health == null
            ? -1
            : client
                ? NetworkStateRevisionTracker.GetAppliedRevision(
                    project,
                    NetworkStateRevisionDomain.Health,
                    entityId)
                : NetworkStateRevisionTracker.GetAuthoritativeRevision(
                    project,
                    NetworkStateRevisionDomain.Health,
                    entityId);
        long damageRevision = damage == null
            ? -1
            : client
                ? NetworkStateRevisionTracker.GetAppliedRevision(
                    project,
                    NetworkStateRevisionDomain.Damage,
                    entityId)
                : NetworkStateRevisionTracker.GetAuthoritativeRevision(
                    project,
                    NetworkStateRevisionDomain.Damage,
                    entityId);
        long lifecycleRevision = client
            ? EntityLifecycleTracker.GetAppliedRevision(project, entityId)
            : EntityLifecycleTracker.GetAuthoritativeRevision(project, entityId);
        return new HealthStateSnapshotV2(
            entityId,
            entity != null,
            entity?.ValuesDictionary?.DatabaseObject?.Name,
            player != null,
            player?.PlayerData?.PlayerIndex,
            health?.Health,
            health?.Health <= 0f,
            health?.DeathTime.HasValue ?? false,
            string.IsNullOrEmpty(health?.CauseOfDeath) ? null : health.CauseOfDeath,
            healthRevision,
            damage?.Hitpoints,
            damageRevision,
            lifecycleRevision);
    }

    private static StateRevisionMetricsSnapshotV2 BuildStateRevisionMetricsSnapshotV2(
        Project project) {
        NetworkStateRevisionMetrics metrics = NetworkStateRevisionTracker.GetMetrics(project);
        return new StateRevisionMetricsSnapshotV2(
            metrics.AuthoritativeTrackedStates,
            metrics.ClientTrackedStates,
            metrics.AppliedStates,
            metrics.IgnoredStaleStates,
            metrics.IgnoredLifecycleStates);
    }

    private static EntitySnapshotV2 BuildEntitySnapshotV2(Project project, int entityId) {
        Entity? entity = project.FindEntity(entityId);
        ComponentBody? body = entity?.FindComponent<ComponentBody>();
        ComponentHealth? health = entity?.FindComponent<ComponentHealth>();
        ComponentDamage? damage = entity?.FindComponent<ComponentDamage>();
        long revision = NetworkManager.IsClientRunning
            ? EntityLifecycleTracker.GetAppliedRevision(project, entityId)
            : EntityLifecycleTracker.GetAuthoritativeRevision(project, entityId);
        bool tombstone = entity == null
            && revision >= 0
            && (NetworkManager.IsClientRunning
                ? !EntityLifecycleTracker.IsPresent(project, entityId)
                : revision > 0);
        return new EntitySnapshotV2(
            entityId,
            entity != null,
            entity?.ValuesDictionary?.DatabaseObject?.Name,
            entity?.FindComponent<ComponentCreature>() != null,
            entity?.FindComponent<ComponentPlayer>() != null,
            body == null
                ? null
                : new EntityBodySnapshotV2(
                    ToVector(body.Position),
                    ToVector(body.Velocity),
                    ToQuaternion(body.Rotation)),
            health?.Health,
            damage?.Hitpoints,
            revision,
            tombstone);
    }

    private static EntityLifecycleSnapshotV2 BuildEntityLifecycleSnapshotV2(Project project) {
        EntityLifecycleMetrics metrics = EntityLifecycleTracker.GetMetrics(project);
        return new EntityLifecycleSnapshotV2(
            metrics.AuthoritativeTrackedEntities,
            metrics.ClientTrackedEntities,
            metrics.AppliedLoads,
            metrics.AppliedRemovals,
            metrics.IgnoredStalePackets,
            metrics.Tombstones);
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
