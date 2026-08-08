using System.Globalization;
using Engine;
using Game;
using Game.Network;
using GameEntitySystem;
using Survivalcraft.Testing.Control;
using Survivalcraft.Testing.Snapshots;

namespace Survivalcraft.TestAutomation;

public partial class TestAutomationModLoader {
    private const int FurnitureHammerObservationDeadlineFrames = 1200;

    private PendingFurnitureHammerAction? pendingFurnitureHammerAction;

    private sealed record PendingFurnitureHammerAction(
        string ActionId,
        long NetworkRequestId,
        int PlayerIndex,
        TerrainCellCoordinate SourceCell,
        FurnitureHammerResultCode ExpectedResult,
        int InitialFurniturePickableCount,
        int DeadlineFrame,
        ActionProvenance Provenance,
        int DuplicateCount,
        NetworkFurnitureHammerResult? Result = null);

    private FurnitureHammerFixture PrepareFurnitureHammerFixture(
        PrepareFurnitureHammerFixtureCommand command) {
        EnsureServerFixtureRole("Furniture hammer fixtures");
        EnsureNoTypedPlayerActionInProgress();
        if (!Enum.IsDefined(command.Mode)) {
            throw new AutomationCommandException(
                "invalid_payload",
                "Furniture hammer fixture mode is invalid.");
        }
        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        SubsystemPlayers players = project.FindSubsystem<SubsystemPlayers>(true);
        SubsystemTerrain terrain = project.FindSubsystem<SubsystemTerrain>(true);
        ComponentPlayer actor = ResolvePlayer(
            players,
            command.PlayerIndex,
            requireMainPlayer: false);
        if (actor.PlayerData.IsMainPlayer) {
            throw new AutomationCommandException(
                "invalid_actor",
                "The furniture hammer fixture expects a connected remote player.");
        }
        TerrainCellCoordinate sourceCell = command.SourceCell
            ?? CreateDefaultFurnitureHammerCell(players);
        ValidateTerrainCoordinate(sourceCell);
        ClearFurnitureHammerSourceArea(terrain, sourceCell);

        int designMaterialValue = Terrain.MakeBlockValue(PlanksBlock.Index);
        int sourceValue = command.Mode == FurnitureHammerFixtureMode.InvalidSource
            ? 0
            : designMaterialValue;
        terrain.ChangeCell(sourceCell.X, sourceCell.Y, sourceCell.Z, sourceValue);
        terrain.TerrainUpdater.RequestSynchronousUpdate();

        IInventory inventory = actor.ComponentMiner.Inventory
            ?? throw new AutomationCommandException(
                "invalid_state",
                "Player inventory is unavailable.");
        int hammerValue = Terrain.MakeBlockValue(IronHammerBlock.Index);
        int equippedValue = command.Mode == FurnitureHammerFixtureMode.MissingTool
            ? Terrain.MakeBlockValue(DirtBlock.Index)
            : hammerValue;
        FillFurnitureHammerInventory(inventory, equippedValue);
        long inventoryRevision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
            project,
            NetworkStateRevisionDomain.Inventory,
            inventory.Id);
        QueueFixtureSynchronization(new InventorySyncPacket(inventory));

        Vector3 playerPosition = command.Mode == FurnitureHammerFixtureMode.OutOfRange
            ? new Vector3(sourceCell.X + 20.5f, sourceCell.Y + 1f, sourceCell.Z + 20.5f)
            : new Vector3(sourceCell.X + 0.5f, sourceCell.Y + 1f, sourceCell.Z + 3f);
        PrepareInventoryActor(actor, playerPosition);
        QueueFixtureSynchronization(new PlayerBodyUpdatePacket(actor, forceFull: true));
        FurnitureHammerPacket.ClearPlayerState(command.PlayerIndex);

        SubsystemPickables pickables = project.FindSubsystem<SubsystemPickables>(true);
        SubsystemFurnitureBlockBehavior furniture =
            project.FindSubsystem<SubsystemFurnitureBlockBehavior>(true);
        int initialPickables = pickables.Pickables.Count(pickable =>
            Terrain.ExtractContents(pickable.Value) == FurnitureBlock.Index);
        int initialDesigns = furniture.m_furnitureDesigns.Count(design => design != null);
        _ = inventoryRevision;
        return new FurnitureHammerFixture(
            command.PlayerIndex,
            actor.Entity.Id,
            inventory.Id,
            command.Mode,
            sourceCell,
            ToVector(playerPosition),
            sourceValue,
            designMaterialValue,
            equippedValue,
            initialPickables,
            initialDesigns);
    }

    private ControlAck ApplyFurnitureHammerFixture(
        ApplyFurnitureHammerFixtureCommand command) {
        FurnitureHammerFixture fixture = command.Fixture
            ?? throw new AutomationCommandException(
                "invalid_payload",
                "Furniture hammer fixture payload is required.");
        EnsureClientActionRole("Furniture hammer actor fixtures");
        EnsureNoTypedPlayerActionInProgress();
        if (!Enum.IsDefined(fixture.Mode)
            || fixture.PlayerEntityId <= 0
            || fixture.PlayerInventoryId <= 0
            || fixture.DesignMaterialValue <= 0
            || fixture.EquippedValue <= 0) {
            throw new AutomationCommandException(
                "invalid_payload",
                "Furniture hammer fixture data is invalid.");
        }
        ValidateTerrainCoordinate(fixture.SourceCell);
        Vector3 position = ToVector(fixture.PlayerPosition);
        if (!IsFinite(position)) {
            throw new AutomationCommandException(
                "invalid_payload",
                "Furniture hammer actor position is invalid.");
        }
        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        ComponentPlayer actor = ResolvePlayer(
            project.FindSubsystem<SubsystemPlayers>(true),
            fixture.PlayerIndex,
            requireMainPlayer: true);
        IInventory inventory = actor.ComponentMiner.Inventory
            ?? throw new AutomationCommandException(
                "invalid_state",
                "Player inventory is unavailable.");
        if (actor.Entity.Id != fixture.PlayerEntityId
            || inventory.Id != fixture.PlayerInventoryId) {
            throw new AutomationCommandException(
                "fixture_mismatch",
                "Furniture hammer fixture actor or inventory does not match this client.");
        }
        FillFurnitureHammerInventory(inventory, fixture.EquippedValue);
        PrepareInventoryActor(actor, position);
        QueueFixtureSynchronization(new PlayerBodyUpdatePacket(actor, forceFull: true));
        FurnitureHammerResultTracker.Reset();
        return new ControlAck(
            true,
            $"Applied furniture hammer fixture for player {fixture.PlayerIndex}.");
    }

    private ActionReceipt QueueFurnitureHammer(
        AutomationCommandContext context,
        FurnitureHammerCommand command) {
        FurnitureHammerFixture fixture = command.Fixture
            ?? throw new AutomationCommandException(
                "invalid_payload",
                "Furniture hammer fixture payload is required.");
        EnsureClientActionRole("Furniture hammer actions");
        EnsureNoTypedPlayerActionInProgress();
        if (!Enum.IsDefined(command.ExpectedResult)
            || !Enum.IsDefined(fixture.Mode)
            || command.DuplicateCount is < 1 or > 8
            || command.PlayerIndex != fixture.PlayerIndex) {
            throw new AutomationCommandException(
                "invalid_payload",
                "Furniture hammer action parameters are invalid.");
        }
        if (command.ProbeRateLimit
            && command.ExpectedResult != FurnitureHammerExpectedResult.RateLimited) {
            throw new AutomationCommandException(
                "invalid_payload",
                "A rate-limit probe must expect the rateLimited result.");
        }
        ValidateTerrainCoordinate(fixture.SourceCell);
        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        ComponentPlayer actor = ResolvePlayer(
            project.FindSubsystem<SubsystemPlayers>(true),
            command.PlayerIndex,
            requireMainPlayer: true);
        if (actor.Entity.Id != fixture.PlayerEntityId) {
            throw new AutomationCommandException(
                "fixture_mismatch",
                "Furniture hammer fixture does not belong to this client player.");
        }
        SubsystemTerrain terrain = project.FindSubsystem<SubsystemTerrain>(true);
        int forgedMaterialValue = Terrain.MakeBlockValue(GraniteBlock.Index);
        int materialValue = command.ForgeDesign
            ? forgedMaterialValue
            : fixture.DesignMaterialValue;
        FurnitureDesign design = CreateFurnitureHammerDesign(terrain, materialValue);
        CellFace start = new(
            fixture.SourceCell.X,
            fixture.SourceCell.Y,
            fixture.SourceCell.Z,
            0);

        if (command.ProbeRateLimit) {
            int warmupMaterial = materialValue == forgedMaterialValue
                ? Terrain.MakeBlockValue(BasaltBlock.Index)
                : forgedMaterialValue;
            FurnitureHammerPacket warmup = new(
                CreateFurnitureHammerDesign(terrain, warmupMaterial),
                start);
            QueueFaultInjection(warmup);
        }

        FurnitureHammerPacket packet = new(design, start);
        int initialPickables = project.FindSubsystem<SubsystemPickables>(true)
            .Pickables.Count(pickable =>
                Terrain.ExtractContents(pickable.Value) == FurnitureBlock.Index);
        FurnitureHammerResultCode expected = ToNetworkFurnitureHammerResult(
            command.ExpectedResult);
        ActionProvenance provenance = command.ForgeDesign
            || command.ProbeRateLimit
            || expected != FurnitureHammerResultCode.Accepted
                ? ActionProvenance.FaultInjection
                : ActionProvenance.ProductionRequest;
        pendingFurnitureHammerAction = new PendingFurnitureHammerAction(
            context.RequestId,
            packet.RequestId,
            command.PlayerIndex,
            fixture.SourceCell,
            expected,
            initialPickables,
            checked(Time.FrameIndex + FurnitureHammerObservationDeadlineFrames),
            provenance,
            command.DuplicateCount);

        RecordFurnitureHammerAction(
            pendingFurnitureHammerAction,
            ActionEventPhase.Queued,
            "queued");
        if (provenance == ActionProvenance.ProductionRequest) {
            QueueProductionRequest(packet);
            for (int index = 1; index < command.DuplicateCount; index++) {
                QueueFaultInjection(packet);
            }
        }
        else {
            for (int index = 0; index < command.DuplicateCount; index++) {
                QueueFaultInjection(packet);
            }
        }
        ScheduleFurnitureHammerObservation(pendingFurnitureHammerAction);
        return new ActionReceipt(
            context.RequestId,
            true,
            Time.FrameIndex,
            command.PlayerIndex,
            "queued",
            $"networkRequestId={packet.RequestId.ToString(CultureInfo.InvariantCulture)}",
            provenance,
            ProductionPathEntries.FurnitureHammer);
    }

    private void ScheduleFurnitureHammerObservation(PendingFurnitureHammerAction pending) {
        Time.QueueFrameIndexDelayedExecution(checked(Time.FrameIndex + 1), () => {
            if (pendingFurnitureHammerAction?.ActionId != pending.ActionId) {
                return;
            }
            PendingFurnitureHammerAction current = pendingFurnitureHammerAction;
            if (current.Result == null
                && FurnitureHammerResultTracker.TryTake(
                    current.NetworkRequestId,
                    out NetworkFurnitureHammerResult observed)) {
                if (observed.Code != current.ExpectedResult) {
                    RecordFurnitureHammerAction(
                        current with { Result = observed },
                        ActionEventPhase.Rejected,
                        $"unexpected_result_{observed.Code.ToString().ToLowerInvariant()}");
                    pendingFurnitureHammerAction = null;
                    return;
                }
                current = current with { Result = observed };
                pendingFurnitureHammerAction = current;
                if (observed.Code != FurnitureHammerResultCode.Accepted) {
                    RecordFurnitureHammerAction(
                        current,
                        ActionEventPhase.Applied,
                        $"production_result_{observed.Code.ToString().ToLowerInvariant()}");
                    pendingFurnitureHammerAction = null;
                    return;
                }
            }
            if (current.Result is NetworkFurnitureHammerResult result
                && result.Code == FurnitureHammerResultCode.Accepted
                && IsFurnitureHammerAcceptedStateSatisfied(current, result)) {
                RecordFurnitureHammerAction(
                    current,
                    ActionEventPhase.Applied,
                    "authoritative_furniture_observed");
                pendingFurnitureHammerAction = null;
                return;
            }
            if (Time.FrameIndex >= current.DeadlineFrame) {
                RecordFurnitureHammerAction(
                    current,
                    ActionEventPhase.Rejected,
                    current.Result == null
                        ? "result_not_observed"
                        : "authoritative_state_not_observed");
                pendingFurnitureHammerAction = null;
                return;
            }
            ScheduleFurnitureHammerObservation(current);
        });
    }

    private static bool IsFurnitureHammerAcceptedStateSatisfied(
        PendingFurnitureHammerAction pending,
        NetworkFurnitureHammerResult result) {
        Project project = GameManager.Project;
        if (project == null) {
            return false;
        }
        SubsystemTerrain terrain = project.FindSubsystem<SubsystemTerrain>();
        SubsystemFurnitureBlockBehavior furniture =
            project.FindSubsystem<SubsystemFurnitureBlockBehavior>();
        SubsystemPickables pickables = project.FindSubsystem<SubsystemPickables>();
        if (terrain == null || furniture == null || pickables == null) {
            return false;
        }
        int sourceValue = terrain.Terrain.GetCellValue(
            pending.SourceCell.X,
            pending.SourceCell.Y,
            pending.SourceCell.Z);
        int matchingPickables = pickables.Pickables.Count(pickable =>
            Terrain.ReplaceLight(pickable.Value, 0)
                == Terrain.ReplaceLight(result.ProducedValue, 0)
            && pickable.Count == result.ProducedCount);
        int furniturePickables = pickables.Pickables.Count(pickable =>
            Terrain.ExtractContents(pickable.Value) == FurnitureBlock.Index);
        return Terrain.ExtractContents(sourceValue) == 0
            && furniture.GetDesign(result.DesignIndex) != null
            && matchingPickables == 1
            && furniturePickables == pending.InitialFurniturePickableCount + 1;
    }

    private void RecordFurnitureHammerAction(
        PendingFurnitureHammerAction pending,
        ActionEventPhase phase,
        string code) {
        NetworkFurnitureHammerResult? result = pending.Result;
        actionEventJournal.Record(
            Time.FrameIndex,
            pending.ActionId,
            nodeRole,
            AutomationCommandDomain.Inventory,
            "furnitureHammer",
            phase,
            pending.PlayerIndex,
            FormatCell(pending.SourceCell),
            code,
            new Dictionary<string, string> {
                ["networkRequestId"] = pending.NetworkRequestId.ToString(CultureInfo.InvariantCulture),
                ["expectedResult"] = pending.ExpectedResult.ToString(),
                ["actualResult"] = result?.Code.ToString() ?? string.Empty,
                ["designIndex"] = result?.DesignIndex.ToString(CultureInfo.InvariantCulture) ?? "-1",
                ["producedValue"] = result?.ProducedValue.ToString(CultureInfo.InvariantCulture) ?? "0",
                ["producedCount"] = result?.ProducedCount.ToString(CultureInfo.InvariantCulture) ?? "0",
                ["duplicateCount"] = pending.DuplicateCount.ToString(CultureInfo.InvariantCulture)
            },
            provenance: pending.Provenance,
            productionEntry: ProductionPathEntries.FurnitureHammer);
    }

    private static FurnitureHammerResultCode ToNetworkFurnitureHammerResult(
        FurnitureHammerExpectedResult result) => result switch {
            FurnitureHammerExpectedResult.Accepted => FurnitureHammerResultCode.Accepted,
            FurnitureHammerExpectedResult.InvalidRequest => FurnitureHammerResultCode.InvalidRequest,
            FurnitureHammerExpectedResult.RateLimited => FurnitureHammerResultCode.RateLimited,
            FurnitureHammerExpectedResult.InvalidDesign => FurnitureHammerResultCode.InvalidDesign,
            FurnitureHammerExpectedResult.InvalidTarget => FurnitureHammerResultCode.InvalidTarget,
            FurnitureHammerExpectedResult.ToolNotEquipped => FurnitureHammerResultCode.ToolNotEquipped,
            FurnitureHammerExpectedResult.ToolBroken => FurnitureHammerResultCode.ToolBroken,
            FurnitureHammerExpectedResult.OutOfRange => FurnitureHammerResultCode.OutOfRange,
            FurnitureHammerExpectedResult.InvalidSource => FurnitureHammerResultCode.InvalidSource,
            FurnitureHammerExpectedResult.DesignMismatch => FurnitureHammerResultCode.DesignMismatch,
            FurnitureHammerExpectedResult.RegistryFull => FurnitureHammerResultCode.RegistryFull,
            FurnitureHammerExpectedResult.CommitFailed => FurnitureHammerResultCode.CommitFailed,
            FurnitureHammerExpectedResult.ClientStateUnavailable => FurnitureHammerResultCode.ClientStateUnavailable,
            _ => throw new AutomationCommandException(
                "invalid_payload",
                "Unknown expected furniture hammer result.")
        };

    private static FurnitureDesign CreateFurnitureHammerDesign(
        SubsystemTerrain terrain,
        int materialValue) {
        FurnitureDesign design = new(terrain);
        int[] values = new int[FurnitureDesign.MinResolution
            * FurnitureDesign.MinResolution
            * FurnitureDesign.MinResolution];
        values[0] = materialValue;
        design.SetValues(FurnitureDesign.MinResolution, values);
        design.Name = "Automation Furniture";
        design.InteractionMode = FurnitureInteractionMode.None;
        return design;
    }

    private static TerrainCellCoordinate CreateDefaultFurnitureHammerCell(
        SubsystemPlayers players) {
        Vector3 spawn = players.GlobalSpawnPosition;
        int chunkMinX = ((int)MathF.Floor(spawn.X) >> 4) << 4;
        int chunkMinZ = ((int)MathF.Floor(spawn.Z) >> 4) << 4;
        return new TerrainCellCoordinate(
            chunkMinX + 6,
            Math.Clamp((int)MathF.Floor(spawn.Y + 2f), 2, 252),
            chunkMinZ + 13);
    }

    private static void ClearFurnitureHammerSourceArea(
        SubsystemTerrain terrain,
        TerrainCellCoordinate source) {
        for (int x = source.X - 1; x <= source.X + 1; x++) {
            for (int y = source.Y - 1; y <= source.Y + 1; y++) {
                for (int z = source.Z - 1; z <= source.Z + 1; z++) {
                    terrain.ChangeCell(x, y, z, 0);
                }
            }
        }
    }

    private static void FillFurnitureHammerInventory(IInventory inventory, int equippedValue) {
        int fillerValue = Terrain.MakeBlockValue(DirtBlock.Index);
        for (int slot = 0; slot < inventory.SlotsCount; slot++) {
            int capacity = inventory.GetSlotCapacity(slot, fillerValue);
            SetInventorySlot(
                inventory,
                slot,
                capacity > 0 ? fillerValue : 0,
                Math.Max(capacity, 0));
        }
        SetInventorySlot(inventory, 0, equippedValue, 1);
        inventory.ActiveSlotIndex = 0;
    }

    private static FurnitureDesignSnapshotV2[] BuildFurnitureDesignSnapshotsV2(
        Project project) => project.FindSubsystem<SubsystemFurnitureBlockBehavior>(true)
        .m_furnitureDesigns
        .Where(design => design != null)
        .OrderBy(design => design.Index)
        .Select(design => new FurnitureDesignSnapshotV2(
            design.Index,
            design.Resolution,
            design.Name,
            design.InteractionMode.ToString(),
            design.m_values.Count(value => Terrain.ExtractContents(value) != 0),
            FurnitureDesignFingerprint(design)))
        .ToArray();

    private static string FurnitureDesignFingerprint(FurnitureDesign design) {
        unchecked {
            ulong hash = 1469598103934665603UL;
            hash = (hash ^ (uint)design.Resolution) * 1099511628211UL;
            foreach (int value in design.m_values) {
                hash = (hash ^ (uint)Terrain.ReplaceLight(value, 0)) * 1099511628211UL;
            }
            return hash.ToString("X16", CultureInfo.InvariantCulture);
        }
    }

    private void ResetFurnitureHammerActions() {
        pendingFurnitureHammerAction = null;
        FurnitureHammerResultTracker.Reset();
    }
}
