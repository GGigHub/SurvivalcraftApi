using System.Globalization;
using Engine;
using Game;
using Game.Network;
using GameEntitySystem;
using Survivalcraft.Testing.Control;
using Survivalcraft.Testing.Snapshots;

namespace Survivalcraft.TestAutomation;

public partial class TestAutomationModLoader {
    private const int MaximumLogicPacketDuplicates = 8;

    private static readonly (LogicElementKind Kind, int X, int Z, int Contents, int Data)[]
        LogicFixtureLayout = [
            (LogicElementKind.Battery, -2, 0, BatteryBlock.Index, 0),
            (LogicElementKind.Button, 0, 0, ButtonBlock.Index, 4),
            (LogicElementKind.Switch, 2, -2, SwitchBlock.Index, 8),
            (LogicElementKind.AdjustableDelayGate, -2, 2, AdjustableDelayGateBlock.Index, 16),
            (LogicElementKind.Piston, 0, 2, PistonBlock.Index, 66),
            (LogicElementKind.MemoryBank, 2, 2, MemoryBankBlock.Index, 16),
            (LogicElementKind.TruthTable, -4, 2, TruthTableCircuitBlock.Index, 16),
            (LogicElementKind.Sign, 4, 2, WoodenPostedSignBlock.Index, 0),
            (LogicElementKind.Dispenser, 4, 0, DispenserBlock.Index, 0)
        ];

    private void ResetLogicFixtures() {
    }

    private LogicFixture PrepareLogicFixture(PrepareLogicFixtureCommand command) {
        EnsureLogicServerRole("Logic fixtures");
        if (string.IsNullOrWhiteSpace(command.Alias) || command.Alias.Length > 64) {
            throw new AutomationCommandException(
                "invalid_payload",
                "Logic fixture alias must contain between 1 and 64 characters.");
        }
        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        SubsystemPlayers players = project.FindSubsystem<SubsystemPlayers>(true);
        SubsystemTerrain terrain = project.FindSubsystem<SubsystemTerrain>(true);
        ComponentPlayer actor = ResolvePlayer(players, command.PlayerIndex, requireMainPlayer: false);
        if (actor.PlayerData.IsMainPlayer) {
            throw new AutomationCommandException(
                "invalid_actor",
                "Logic fixtures require a connected remote player.");
        }

        int aliasOffset = command.Alias.Aggregate(
            0,
            (hash, character) => unchecked(hash * 31 + character)) & 3;
        aliasOffset *= 32;
        int baseX = Terrain.ToCell(players.GlobalSpawnPosition.X) + 12 + aliasOffset;
        int baseY = Math.Clamp(Terrain.ToCell(players.GlobalSpawnPosition.Y) + 1, 4, 248);
        int baseZ = Terrain.ToCell(players.GlobalSpawnPosition.Z) + 12;
        List<LogicFixtureElement> elements = [];
        foreach ((LogicElementKind kind, int offsetX, int offsetZ, int contents, int data)
                 in LogicFixtureLayout) {
            TerrainCellCoordinate cell = new(baseX + offsetX, baseY, baseZ + offsetZ);
            PrepareLogicCell(terrain, cell, Terrain.MakeBlockValue(contents, 0, data));
            elements.Add(CreateLogicFixtureElement(kind, cell, data));
        }

        LogicFixtureElement piston = elements.Single(item => item.Kind == LogicElementKind.Piston);
        Point3 pistonPoint = ToPoint(piston.Cell);
        Point3 pistonDirection = CellFace.FaceToPoint3(PistonBlock.GetFace(piston.InitialData));
        TerrainCellCoordinate pushCell = ToCell(pistonPoint + pistonDirection);
        for (int distance = 1; distance <= 9; distance++) {
            Point3 point = pistonPoint + pistonDirection * distance;
            terrain.ChangeCell(point.X, point.Y, point.Z, 0);
        }
        terrain.ChangeCell(
            pushCell.X,
            pushCell.Y,
            pushCell.Z,
            Terrain.MakeBlockValue(GraniteBlock.Index));

        TerrainCellCoordinate farBattery = new(baseX + 14, baseY, baseZ);
        PrepareLogicCell(
            terrain,
            farBattery,
            Terrain.MakeBlockValue(BatteryBlock.Index, 0, 0));
        TerrainCellCoordinate persistentVoltageCell = new(baseX - 4, baseY, baseZ);
        PrepareLogicCell(
            terrain,
            persistentVoltageCell,
            Terrain.MakeBlockValue(CounterBlock.Index, 0, 16));

        SubsystemMemoryBankBlockBehavior memory =
            project.FindSubsystem<SubsystemMemoryBankBlockBehavior>(true);
        SubsystemTruthTableCircuitBlockBehavior truth =
            project.FindSubsystem<SubsystemTruthTableCircuitBlockBehavior>(true);
        SubsystemSignBlockBehavior signs =
            project.FindSubsystem<SubsystemSignBlockBehavior>(true);
        memory.SetBlockData(
            ToPoint(elements.Single(item => item.Kind == LogicElementKind.MemoryBank).Cell),
            new MemoryBankData());
        truth.SetBlockData(
            ToPoint(elements.Single(item => item.Kind == LogicElementKind.TruthTable).Cell),
            new TruthTableData());
        signs.SetSignData(
            ToPoint(elements.Single(item => item.Kind == LogicElementKind.Sign).Cell),
            [string.Empty, string.Empty, string.Empty, string.Empty],
            [Color.Black, Color.Black, Color.Black, Color.Black],
            string.Empty);

        Vector3 playerPosition = new(baseX + 0.5f, baseY + 1.05f, baseZ - 1.5f);
        PrepareLogicPlayer(actor, playerPosition);
        foreach (ComponentPlayer other in players.ComponentPlayers.Where(player =>
                     player != actor && !player.PlayerData.IsMainPlayer)) {
            Vector3 farPosition = new(baseX + 24.5f, baseY + 1.05f, baseZ + 24.5f);
            PrepareLogicPlayer(other, farPosition);
            QueueFixtureSynchronization(new PlayerBodyUpdatePacket(other, forceFull: true));
        }
        QueueFixtureSynchronization(new PlayerBodyUpdatePacket(actor, forceFull: true));
        terrain.TerrainUpdater.SetUpdateLocation(
            actor.PlayerData.PlayerIndex,
            playerPosition.XZ,
            48f,
            64f);
        terrain.TerrainUpdater.RequestSynchronousUpdate();

        int editedSwitchData = SwitchBlock.SetVoltageLevel(8, 5);
        int toggledSwitchData = Terrain.ExtractData(
            SwitchBlock.SetLeverState(
                Terrain.MakeBlockValue(SwitchBlock.Index, 0, editedSwitchData),
                true));
        return new LogicFixture(
            command.Alias,
            command.PlayerIndex,
            actor.Entity.Id,
            ToVector(playerPosition),
            elements.ToArray(),
            pushCell,
            farBattery,
            persistentVoltageCell,
            8,
            toggledSwitchData);
    }

    private ControlAck ApplyLogicFixture(ApplyLogicFixtureCommand command) {
        LogicFixture fixture = command.Fixture
            ?? throw new AutomationCommandException("invalid_payload", "Fixture payload is required.");
        if (!nodeRole.StartsWith("client", StringComparison.Ordinal)
            || !NetworkManager.IsClientRunning) {
            throw new AutomationCommandException(
                "permission_denied",
                "Logic actor fixtures must be applied by a client node.");
        }
        if (fixture.Elements is not { Length: > 0 and <= 64 }) {
            throw new AutomationCommandException("invalid_payload", "Logic fixture is empty or oversized.");
        }
        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        ComponentPlayer actor = ResolvePlayer(
            project.FindSubsystem<SubsystemPlayers>(true),
            fixture.PlayerIndex,
            requireMainPlayer: true);
        SubsystemTerrain terrain = project.FindSubsystem<SubsystemTerrain>(true);
        Vector3 position = ToVector(fixture.PlayerPosition);
        if (!IsFinite(position)) {
            throw new AutomationCommandException("invalid_payload", "Fixture player position is invalid.");
        }
        PrepareLogicPlayer(actor, position);
        terrain.TerrainUpdater.SetUpdateLocation(
            fixture.PlayerIndex,
            position.XZ,
            48f,
            64f);
        terrain.TerrainUpdater.RequestSynchronousUpdate();
        foreach (LogicFixtureElement element in fixture.Elements) {
            int contents = LogicFixtureLayout.Single(item => item.Kind == element.Kind).Contents;
            PrepareLogicCell(
                terrain,
                element.Cell,
                Terrain.MakeBlockValue(contents, 0, element.InitialData));
        }
        Point3 pistonPoint = ToPoint(fixture.Elements.Single(element =>
            element.Kind == LogicElementKind.Piston).Cell);
        Point3 pistonDirection = fixture.PistonPushCell == ToCell(pistonPoint)
            ? Point3.Zero
            : ToPoint(fixture.PistonPushCell) - pistonPoint;
        if (pistonDirection != Point3.Zero) {
            for (int distance = 1; distance <= 9; distance++) {
                Point3 point = pistonPoint + pistonDirection * distance;
                terrain.ChangeCell(point.X, point.Y, point.Z, 0);
            }
            terrain.ChangeCell(
                fixture.PistonPushCell.X,
                fixture.PistonPushCell.Y,
                fixture.PistonPushCell.Z,
                Terrain.MakeBlockValue(GraniteBlock.Index));
        }
        PrepareLogicCell(
            terrain,
            fixture.FarBatteryCell,
            Terrain.MakeBlockValue(BatteryBlock.Index));
        PrepareLogicCell(
            terrain,
            fixture.PersistentVoltageCell,
            Terrain.MakeBlockValue(CounterBlock.Index, 0, 16));
        SubsystemMemoryBankBlockBehavior memory =
            project.FindSubsystem<SubsystemMemoryBankBlockBehavior>(true);
        SubsystemTruthTableCircuitBlockBehavior truth =
            project.FindSubsystem<SubsystemTruthTableCircuitBlockBehavior>(true);
        SubsystemSignBlockBehavior signs =
            project.FindSubsystem<SubsystemSignBlockBehavior>(true);
        memory.SetBlockData(
            ToPoint(fixture.Elements.Single(element =>
                element.Kind == LogicElementKind.MemoryBank).Cell),
            new MemoryBankData());
        truth.SetBlockData(
            ToPoint(fixture.Elements.Single(element =>
                element.Kind == LogicElementKind.TruthTable).Cell),
            new TruthTableData());
        signs.SetSignData(
            ToPoint(fixture.Elements.Single(element =>
                element.Kind == LogicElementKind.Sign).Cell),
            [string.Empty, string.Empty, string.Empty, string.Empty],
            [Color.Black, Color.Black, Color.Black, Color.Black],
            string.Empty);
        terrain.TerrainUpdater.RequestSynchronousUpdate();
        QueueFixtureSynchronization(new PlayerBodyUpdatePacket(actor, forceFull: true));
        return new ControlAck(true, $"Prepared logic actor {fixture.PlayerIndex}.");
    }

    private ActionReceipt QueueLogicEdit(
        AutomationCommandContext context,
        LogicEditCommand command) {
        EnsureLogicClientRole("Logic edits");
        if (!Enum.IsDefined(command.Kind)) {
            throw new AutomationCommandException("invalid_payload", "Unknown logic element kind.");
        }
        ValidateLogicDuplicateCount(command.DuplicateCount);
        ValidateTerrainCoordinate(command.Cell);
        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        ComponentPlayer actor = ResolvePlayer(
            project.FindSubsystem<SubsystemPlayers>(true),
            command.PlayerIndex,
            requireMainPlayer: true);
        Point3 point = ToPoint(command.Cell);
        CellFace cellFace = new(point, 0);
        Packet packet = CreateLogicEditPacket(command, cellFace);
        if (packet is not EditableBlockPacketBase editable) {
            throw new AutomationCommandException(
                "invalid_state",
                "Logic edit packet does not expose the authoritative mutation envelope.");
        }
        if (command.ForgeAuthoritativeState) {
            editable.m_packetKind = EditableLogicPacketKind.State;
            editable.m_result = EditableLogicResultCode.Accepted;
            editable.m_revision = long.MaxValue / 4;
            editable.m_expectedRevision = -1;
        }
        if (command.ExpectedRevision.HasValue) {
            editable.m_expectedRevision = command.ExpectedRevision.Value;
        }
        if (command.TerrainRevision.HasValue) {
            editable.m_terrainRevision = command.TerrainRevision.Value;
        }
        for (int i = 0; i < command.DuplicateCount; i++) {
            if (command.ExpectRejected) {
                QueueFaultInjection(packet);
            }
            else {
                QueueProductionRequest(packet);
            }
        }
        RecordLogicAction(
            context.RequestId,
            "edit",
            ActionEventPhase.Queued,
            actor.PlayerData.PlayerIndex,
            command.Cell,
            command.ExpectRejected ? "rejection_probe_queued" : "queued",
            command.Kind.ToString());
        RecordLogicAction(
            context.RequestId,
            "edit",
            ActionEventPhase.Observed,
            actor.PlayerData.PlayerIndex,
            command.Cell,
            command.ExpectRejected ? "rejection_probe_sent" : "production_request_sent",
            command.Kind.ToString());
        return new ActionReceipt(
            context.RequestId,
            true,
            Time.FrameIndex,
            actor.PlayerData.PlayerIndex,
            command.ExpectRejected ? "rejection_probe_queued" : "queued",
            Provenance: command.ExpectRejected
                ? ActionProvenance.FaultInjection
                : ActionProvenance.ProductionRequest,
            ProductionEntry: ProductionPathEntries.LogicEdit);
    }

    private ActionReceipt QueueLogicInteract(
        AutomationCommandContext context,
        LogicInteractCommand command) {
        EnsureLogicClientRole("Logic interactions");
        ValidateLogicDuplicateCount(command.DuplicateCount);
        ValidateTerrainCoordinate(command.Cell);
        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        ComponentPlayer actor = ResolvePlayer(
            project.FindSubsystem<SubsystemPlayers>(true),
            command.PlayerIndex,
            requireMainPlayer: true);
        Point3 interactionPoint = ToPoint(command.Cell);
        Vector3 target = new(
            interactionPoint.X + 0.5f,
            interactionPoint.Y + 0.12f,
            interactionPoint.Z + 0.5f);
        Vector3 origin = actor.ComponentCreatureModel.EyePosition;
        Vector3 delta = target - origin;
        if (delta.LengthSquared() < 0.0001f) {
            throw new AutomationCommandException("invalid_state", "Interaction ray has zero length.");
        }
        PlayerInteractPacket packet = new() {
            m_fromPlayerIndex = command.TargetPlayerIndex ?? command.PlayerIndex,
            m_interactEvent = ComponentPlayer.InteractEvent.Start,
            m_netInteractRay = new Ray3(origin, Vector3.Normalize(delta)),
            m_netInteractRaycast = null,
            m_requestId = NetworkMutationRequestTracker.CreateRequestId()
        };
        for (int i = 0; i < command.DuplicateCount; i++) {
            if (command.ExpectRejected) {
                QueueFaultInjection(packet);
            }
            else {
                QueueProductionRequest(packet);
            }
        }
        RecordLogicAction(
            context.RequestId,
            "interact",
            ActionEventPhase.Queued,
            actor.PlayerData.PlayerIndex,
            command.Cell,
            command.ExpectRejected ? "rejection_probe_queued" : "queued",
            command.TargetPlayerIndex?.ToString(CultureInfo.InvariantCulture) ?? "self");
        RecordLogicAction(
            context.RequestId,
            "interact",
            ActionEventPhase.Observed,
            actor.PlayerData.PlayerIndex,
            command.Cell,
            command.ExpectRejected ? "rejection_probe_sent" : "production_request_sent",
            command.TargetPlayerIndex?.ToString(CultureInfo.InvariantCulture) ?? "self");
        return new ActionReceipt(
            context.RequestId,
            true,
            Time.FrameIndex,
            actor.PlayerData.PlayerIndex,
            command.ExpectRejected ? "rejection_probe_queued" : "queued",
            Provenance: command.ExpectRejected
                ? ActionProvenance.FaultInjection
                : ActionProvenance.ProductionRequest,
            ProductionEntry: ProductionPathEntries.LogicInteract);
    }

    private LogicRevisionBurstResult RunLogicRevisionBurst(
        AutomationCommandContext context,
        RunLogicRevisionBurstCommand command) {
        EnsureLogicServerRole("Logic revision bursts");
        if (command.IntermediateVoltage is < 0 or > 15
            || command.FinalVoltage is < 0 or > 15
            || !float.IsFinite(command.IntermediatePersistentVoltage)
            || !float.IsFinite(command.FinalPersistentVoltage)) {
            throw new AutomationCommandException("invalid_payload", "Logic burst values are invalid.");
        }
        ValidateTerrainCoordinate(command.BatteryCell);
        TerrainCellCoordinate persistentVoltageCell =
            command.PersistentVoltageCell ?? command.BatteryCell;
        ValidateTerrainCoordinate(persistentVoltageCell);
        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        SubsystemTerrain terrain = project.FindSubsystem<SubsystemTerrain>(true);
        Point3 point = ToPoint(command.BatteryCell);
        int currentValue = terrain.Terrain.GetCellValue(point.X, point.Y, point.Z);
        if (Terrain.ExtractContents(currentValue) != BatteryBlock.Index) {
            throw new AutomationCommandException("wrong_block", "Logic burst target is not a battery.");
        }
        Point3 persistentPoint = ToPoint(persistentVoltageCell);
        int persistentContents = Terrain.ExtractContents(
            terrain.Terrain.GetCellValue(
                persistentPoint.X,
                persistentPoint.Y,
                persistentPoint.Z));
        if (persistentContents != CounterBlock.Index
            && persistentContents != SRLatchBlock.Index
            && persistentContents != RandomGeneratorBlock.Index) {
            throw new AutomationCommandException(
                "wrong_block",
                "Persistent-voltage target is not a supported stateful electric block.");
        }
        NetworkLogicStateKey key = NetworkLogicStateKey.Editable(
            NetworkLogicElementKind.Battery,
            false,
            0,
            0,
            point);

        RecordLogicAction(
            context.RequestId,
            "revisionBurst",
            ActionEventPhase.Queued,
            -1,
            command.BatteryCell,
            "revision_burst",
            command.IntermediateVoltage.ToString(CultureInfo.InvariantCulture));

        int intermediateData = BatteryBlock.SetVoltageLevel(
            Terrain.ExtractData(currentValue),
            command.IntermediateVoltage);
        terrain.ChangeCell(
            point.X,
            point.Y,
            point.Z,
            Terrain.ReplaceData(currentValue, intermediateData));
        long staleLogicRevision = NetworkLogicStateTracker.AdvanceAuthoritativeRevision(project, key);
        BatteryVoltagePacket staleLogic = new(
            new CellFace(point, 0),
            false,
            0,
            0,
            command.IntermediateVoltage);
        StampLogicState(staleLogic, staleLogicRevision, terrain, point);

        int intermediateValue = terrain.Terrain.GetCellValue(point.X, point.Y, point.Z);
        int finalData = BatteryBlock.SetVoltageLevel(
            Terrain.ExtractData(intermediateValue),
            command.FinalVoltage);
        terrain.ChangeCell(
            point.X,
            point.Y,
            point.Z,
            Terrain.ReplaceData(intermediateValue, finalData));
        long finalLogicRevision = NetworkLogicStateTracker.AdvanceAuthoritativeRevision(project, key);
        BatteryVoltagePacket finalLogic = new(
            new CellFace(point, 0),
            false,
            0,
            0,
            command.FinalVoltage);
        StampLogicState(finalLogic, finalLogicRevision, terrain, point);

        SubsystemElectricity electricity = project.FindSubsystem<SubsystemElectricity>(true);
        electricity.m_persistentElementsVoltages[persistentPoint] =
            command.IntermediatePersistentVoltage;
        long staleElectricityRevision = NetworkLogicStateTracker.AdvanceAuthoritativeRevision(
            project,
            NetworkLogicStateKey.Electricity());
        SubsystemElectricityPacket staleElectricity = new(electricity, staleElectricityRevision);
        electricity.m_persistentElementsVoltages[persistentPoint] =
            command.FinalPersistentVoltage;
        long finalElectricityRevision = NetworkLogicStateTracker.AdvanceAuthoritativeRevision(
            project,
            NetworkLogicStateKey.Electricity());
        SubsystemElectricityPacket finalElectricity = new(electricity, finalElectricityRevision);

        QueueAuthoritativeState(finalLogic);
        QueueAuthoritativeState(finalElectricity);
        if (command.SendStaleState) {
            QueueFaultInjectionAfterSettle(staleLogic);
            QueueFaultInjectionAfterSettle(staleElectricity);
        }
        RecordLogicAction(
            context.RequestId,
            "revisionBurst",
            ActionEventPhase.Applied,
            -1,
            command.BatteryCell,
            command.SendStaleState ? "stale_states_queued" : "applied",
            command.FinalVoltage.ToString(CultureInfo.InvariantCulture));
        ActionReceipt receipt = new(
            context.RequestId,
            true,
            Time.FrameIndex,
            -1,
            command.SendStaleState ? "stale_states_queued" : "applied",
            Provenance: ActionProvenance.FaultInjection,
            ProductionEntry: ProductionPathEntries.LogicRevision);
        return new LogicRevisionBurstResult(
            receipt,
            command.BatteryCell,
            persistentVoltageCell,
            command.FinalVoltage,
            command.FinalPersistentVoltage,
            staleLogicRevision,
            finalLogicRevision,
            staleElectricityRevision,
            finalElectricityRevision);
    }

    private MovingBlockLifecycleBurstResult RunMovingBlockLifecycleBurst(
        AutomationCommandContext context,
        RunMovingBlockLifecycleBurstCommand command) {
        EnsureLogicServerRole("Moving-block lifecycle bursts");
        ValidateTerrainCoordinate(command.AnchorCell);
        if (string.IsNullOrWhiteSpace(command.Identity) || command.Identity.Length > 128) {
            throw new AutomationCommandException("invalid_payload", "Moving-block identity is invalid.");
        }
        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        SubsystemMovingBlocks moving = project.FindSubsystem<SubsystemMovingBlocks>(true);
        Point3 tag = ToPoint(command.AnchorCell);
        RecordLogicAction(
            context.RequestId,
            "movingLifecycleBurst",
            ActionEventPhase.Queued,
            -1,
            command.AnchorCell,
            "lifecycle_burst",
            command.Identity);
        IMovingBlockSet existing = moving.FindMovingBlocks(command.Identity, tag);
        if (existing != null) {
            moving.RemoveMovingBlockSet(existing);
        }
        Vector3 position = new(tag.X + 0.25f, tag.Y + 3f, tag.Z + 0.25f);
        List<MovingBlock> blocks = [
            new MovingBlock {
                Offset = Point3.Zero,
                Value = Terrain.MakeBlockValue(GlassBlock.Index)
            }
        ];
        SubsystemMovingBlocks.MovingBlockSet first = moving.AddMovingBlockSet(
            position,
            position + new Vector3(8f, 0f, 0f),
            0f,
            0f,
            0f,
            Vector2.Zero,
            blocks,
            command.Identity,
            tag,
            testCollision: false) as SubsystemMovingBlocks.MovingBlockSet
            ?? throw new AutomationCommandException("invalid_state", "Initial moving block was rejected.");
        MovingBlockAddPacket staleAdd = MovingBlockAddPacket.CreateBaseline(first);
        long staleGeneration = staleAdd.m_generation;
        moving.RemoveMovingBlockSet(first);

        SubsystemMovingBlocks.MovingBlockSet replacement = moving.AddMovingBlockSet(
            position + new Vector3(0f, 0.5f, 0f),
            position + new Vector3(8f, 0.5f, 0f),
            0f,
            0f,
            0f,
            Vector2.Zero,
            [
                new MovingBlock {
                    Offset = Point3.Zero,
                    Value = Terrain.MakeBlockValue(GlassBlock.Index)
                }
            ],
            command.Identity,
            tag,
            testCollision: false) as SubsystemMovingBlocks.MovingBlockSet
            ?? throw new AutomationCommandException("invalid_state", "Replacement moving block was rejected.");
        string concurrentIdentity = command.Identity + ".Concurrent";
        for (int index = 0; index < 2; index++) {
            Vector3 concurrentPosition = position + new Vector3(0f, 1.5f, 2f + index * 2f);
            _ = moving.AddMovingBlockSet(
                concurrentPosition,
                concurrentPosition + new Vector3(8f, 0f, 0f),
                0f,
                0f,
                0f,
                Vector2.Zero,
                [
                    new MovingBlock {
                        Offset = Point3.Zero,
                        Value = Terrain.MakeBlockValue(GlassBlock.Index)
                    }
                ],
                concurrentIdentity,
                tag: null,
                testCollision: false) as SubsystemMovingBlocks.MovingBlockSet
                ?? throw new AutomationCommandException(
                    "invalid_state",
                    $"Concurrent moving block {index} was rejected.");
        }
        int concurrentInstances = moving.m_movingBlockSets.Count(set =>
            set.Id == concurrentIdentity && set.Tag == null);
        NetworkLogicStateKey key = NetworkLogicStateKey.MovingBlock(command.Identity, true, tag);
        long finalGeneration = NetworkLogicStateTracker.GetAuthoritativeMovingGeneration(project, key);
        long finalRevision = NetworkLogicStateTracker.GetAuthoritativeRevision(project, key);
        if (command.SendStaleAdd) {
            int staleDeliveryFrame = checked(Time.FrameIndex + 30);
            Time.QueueFrameIndexDelayedExecution(
                staleDeliveryFrame,
                () => QueueFaultInjection(staleAdd));
        }
        RecordLogicAction(
            context.RequestId,
            "movingLifecycleBurst",
            ActionEventPhase.Applied,
            -1,
            command.AnchorCell,
            command.SendStaleAdd ? "stale_add_queued" : "applied",
            command.Identity);
        ActionReceipt receipt = new(
            context.RequestId,
            true,
            Time.FrameIndex,
            -1,
            command.SendStaleAdd ? "stale_add_queued" : "applied",
            Provenance: ActionProvenance.FaultInjection,
            ProductionEntry: ProductionPathEntries.MovingBlockLifecycle);
        return new MovingBlockLifecycleBurstResult(
            receipt,
            command.Identity,
            concurrentIdentity,
            concurrentInstances,
            staleGeneration,
            finalGeneration,
            finalRevision,
            moving.FindMovingBlocks(command.Identity, tag) == replacement);
    }

    private PistonLifecycleResult RunPistonLifecycle(
        AutomationCommandContext context,
        RunPistonLifecycleCommand command) {
        EnsureLogicServerRole("Piston lifecycle actions");
        ValidateTerrainCoordinate(command.PistonCell);
        if (command.Extension is < 1 or > SubsystemPistonBlockBehavior.PistonMaxExtension) {
            throw new AutomationCommandException("invalid_payload", "Piston extension is invalid.");
        }
        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        Point3 point = ToPoint(command.PistonCell);
        SubsystemPistonBlockBehavior pistons =
            project.FindSubsystem<SubsystemPistonBlockBehavior>(true);
        SubsystemMovingBlocks moving = project.FindSubsystem<SubsystemMovingBlocks>(true);
        RecordLogicAction(
            context.RequestId,
            "pistonLifecycle",
            ActionEventPhase.Queued,
            -1,
            command.PistonCell,
            "queued",
            command.Extension.ToString(CultureInfo.InvariantCulture));
        pistons.AdjustPiston(point, command.Extension);
        pistons.ProcessQueuedActions();
        IMovingBlockSet movingSet = moving.FindMovingBlocks(
            SubsystemPistonBlockBehavior.IdString,
            point);
        bool movingObserved = movingSet != null;
        if (command.Stop) {
            for (int i = 0; i < 4 && movingSet != null; i++) {
                pistons.StopPiston(point);
                movingSet = moving.FindMovingBlocks(
                    SubsystemPistonBlockBehavior.IdString,
                    point);
            }
        }
        NetworkLogicStateKey key = NetworkLogicStateKey.MovingBlock(
            SubsystemPistonBlockBehavior.IdString,
            true,
            point);
        long generation = NetworkLogicStateTracker.GetAuthoritativeMovingGeneration(project, key);
        long revision = NetworkLogicStateTracker.GetAuthoritativeRevision(project, key);
        RecordLogicAction(
            context.RequestId,
            "pistonLifecycle",
            ActionEventPhase.Applied,
            -1,
            command.PistonCell,
            command.Stop ? "stopped" : "moving",
            generation.ToString(CultureInfo.InvariantCulture));
        ActionReceipt receipt = new(
            context.RequestId,
            true,
            Time.FrameIndex,
            -1,
            command.Stop ? "stopped" : "moving",
            Provenance: ActionProvenance.AuthoritativeState,
            ProductionEntry: ProductionPathEntries.PistonLifecycle);
        return new PistonLifecycleResult(
            receipt,
            command.PistonCell,
            movingObserved,
            moving.FindMovingBlocks(SubsystemPistonBlockBehavior.IdString, point) != null,
            generation,
            revision);
    }

    private static LogicFixtureElement CreateLogicFixtureElement(
        LogicElementKind kind,
        TerrainCellCoordinate cell,
        int initialData) =>
        kind switch {
            LogicElementKind.Battery => new(
                kind, cell, initialData, 7),
            LogicElementKind.Button => new(
                kind, cell, initialData, 6),
            LogicElementKind.Switch => new(
                kind, cell, initialData, 5),
            LogicElementKind.AdjustableDelayGate => new(
                kind, cell, initialData, 9),
            LogicElementKind.Piston => new(
                kind, cell, initialData, 13634),
            LogicElementKind.MemoryBank => new(
                kind, cell, initialData, 0, "1234ABCD", 1),
            LogicElementKind.TruthTable => new(
                kind, cell, initialData, 0, "F0E1D2C3B4A59687"),
            LogicElementKind.Sign => new(
                kind,
                cell,
                initialData,
                0,
                TargetSignLines: ["North", "East", "South", "West"],
                TargetSignUrl: "https://example.test/sign"),
            LogicElementKind.Dispenser => new(
                kind, cell, initialData, 3),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

    private static Packet CreateLogicEditPacket(
        LogicEditCommand command,
        CellFace cellFace) =>
        command.Kind switch {
            LogicElementKind.AdjustableDelayGate =>
                new AdjustableDelayGatePacket(cellFace, false, 0, 0, command.Value),
            LogicElementKind.Battery =>
                new BatteryVoltagePacket(cellFace, false, 0, 0, command.Value),
            LogicElementKind.Button =>
                new ButtonVoltagePacket(cellFace, false, 0, 0, command.Value),
            LogicElementKind.Switch =>
                new SwitchVoltagePacket(cellFace, false, 0, 0, command.Value),
            LogicElementKind.Piston =>
                new PistonDataPacket(cellFace, false, 0, 0, command.Value),
            LogicElementKind.MemoryBank =>
                new MemoryBankDataPacket(
                    cellFace,
                    false,
                    0,
                    0,
                    CreateMemoryData(command.StructuredData, command.LastOutput)),
            LogicElementKind.TruthTable =>
                new TruthTableDataPacket(
                    cellFace,
                    false,
                    0,
                    0,
                    CreateTruthData(command.StructuredData)),
            LogicElementKind.Sign =>
                new SignBlockPacket(
                    cellFace.Point,
                    command.SignLines ?? [string.Empty, string.Empty, string.Empty, string.Empty],
                    [Color.Red, Color.Green, Color.Blue, Color.White],
                    command.SignUrl ?? string.Empty),
            LogicElementKind.Dispenser =>
                new DispenserPacket(cellFace.Point, checked((byte)command.Value)),
            _ => throw new AutomationCommandException("invalid_payload", "Unknown logic element kind.")
        };

    private static MemoryBankData CreateMemoryData(string? value, byte lastOutput) {
        MemoryBankData data = new() { LastOutput = lastOutput };
        data.LoadString(value ?? string.Empty);
        data.LastOutput = lastOutput;
        return data;
    }

    private static TruthTableData CreateTruthData(string? value) {
        TruthTableData data = new();
        data.LoadString(value ?? string.Empty);
        return data;
    }

    private static void PrepareLogicCell(
        SubsystemTerrain terrain,
        TerrainCellCoordinate cell,
        int value) {
        terrain.ChangeCell(cell.X, cell.Y - 1, cell.Z, Terrain.MakeBlockValue(GraniteBlock.Index));
        for (int y = cell.Y; y <= cell.Y + 3; y++) {
            terrain.ChangeCell(cell.X, y, cell.Z, 0);
        }
        terrain.ChangeCell(cell.X, cell.Y, cell.Z, value);
    }

    private static void PrepareLogicPlayer(ComponentPlayer player, Vector3 position) {
        player.ComponentBody.Position = position;
        player.ComponentBody.Rotation = Quaternion.Identity;
        player.ComponentBody.Velocity = Vector3.Zero;
        player.ComponentBody.IsGravityEnabled = false;
        player.ComponentLocomotion.IsCreativeFlyEnabled = true;
        player.ComponentLocomotion.LookAngles = Vector2.Zero;
        player.ComponentLocomotion.LookOrder = Vector2.Zero;
        player.ComponentLocomotion.TurnOrder = Vector2.Zero;
    }

    private static void StampLogicState(
        EditableBlockPacketBase packet,
        long revision,
        SubsystemTerrain terrain,
        Point3 point) {
        packet.m_packetKind = EditableLogicPacketKind.State;
        packet.m_result = EditableLogicResultCode.Accepted;
        packet.m_expectedRevision = -1;
        packet.m_revision = revision;
        packet.m_terrainRevision = TerrainRevisionTracker.GetAuthoritativeRevision(
            terrain,
            point.X >> 4,
            point.Z >> 4);
    }

    private void RecordLogicAction(
        string actionId,
        string action,
        ActionEventPhase phase,
        int playerIndex,
        TerrainCellCoordinate cell,
        string code,
        string value) {
        (ActionProvenance provenance, string entry) = LogicEvidence(action, code);
        actionEventJournal.Record(
            Time.FrameIndex,
            actionId,
            nodeRole,
            AutomationCommandDomain.Logic,
            action,
            phase,
            playerIndex >= 0 ? playerIndex : null,
            FormatCell(cell),
            code,
            new Dictionary<string, string> {
                ["value"] = value
            },
            provenance: provenance,
            productionEntry: entry);
    }

    private static (ActionProvenance Provenance, string Entry) LogicEvidence(
        string action,
        string code) {
        if (string.Equals(action, "revisionBurst", StringComparison.Ordinal)) {
            return (ActionProvenance.FaultInjection, ProductionPathEntries.LogicRevision);
        }
        if (string.Equals(action, "movingLifecycleBurst", StringComparison.Ordinal)) {
            return (ActionProvenance.FaultInjection, ProductionPathEntries.MovingBlockLifecycle);
        }
        if (string.Equals(action, "pistonLifecycle", StringComparison.Ordinal)) {
            return (ActionProvenance.AuthoritativeState, ProductionPathEntries.PistonLifecycle);
        }
        bool fault = code.Contains("rejection_probe", StringComparison.Ordinal);
        return string.Equals(action, "interact", StringComparison.Ordinal)
            ? (fault ? ActionProvenance.FaultInjection : ActionProvenance.ProductionRequest,
                ProductionPathEntries.LogicInteract)
            : (fault ? ActionProvenance.FaultInjection : ActionProvenance.ProductionRequest,
                ProductionPathEntries.LogicEdit);
    }

    private static void EnsureLogicServerRole(string operation) {
        if (NetworkManager.IsClientRunning || !NetworkManager.IsServerRunning) {
            throw new AutomationCommandException(
                "permission_denied",
                $"{operation} require the authoritative server node.");
        }
    }

    private void EnsureLogicClientRole(string operation) {
        if (!nodeRole.StartsWith("client", StringComparison.Ordinal)
            || !NetworkManager.IsClientRunning) {
            throw new AutomationCommandException(
                "permission_denied",
                $"{operation} must originate from a connected client node.");
        }
    }

    private static void ValidateLogicDuplicateCount(int count) {
        if (count is < 1 or > MaximumLogicPacketDuplicates) {
            throw new AutomationCommandException(
                "invalid_payload",
                $"Duplicate count must be between 1 and {MaximumLogicPacketDuplicates}.");
        }
    }

    private static Point3 ToPoint(TerrainCellCoordinate cell) =>
        new(cell.X, cell.Y, cell.Z);

    private static TerrainCellCoordinate ToCell(Point3 point) =>
        new(point.X, point.Y, point.Z);
}
