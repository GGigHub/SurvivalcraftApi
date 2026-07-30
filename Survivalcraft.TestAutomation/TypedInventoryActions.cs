using System.Globalization;
using Engine;
using Game;
using Game.Network;
using GameEntitySystem;
using Survivalcraft.Testing.Control;
using Survivalcraft.Testing.Snapshots;

namespace Survivalcraft.TestAutomation;

public partial class TestAutomationModLoader {
    private const int InventoryNetworkObservationDeadlineFrames = 1200;

    private PendingInventoryAction? pendingInventoryAction;

    private enum InventoryActionKind {
        Move,
        SelectSlot,
        Drop
    }

    private sealed record PendingInventoryAction(
        string ActionId,
        InventoryActionKind Kind,
        int PlayerIndex,
        int PrimaryInventoryId,
        int PrimarySlotIndex,
        int ExpectedPrimaryCount,
        int SecondaryInventoryId,
        int SecondarySlotIndex,
        int ExpectedSecondaryCount,
        int ExpectedActiveSlot,
        long MinimumRevision,
        int DeadlineFrame);

    private InventoryFixture PrepareInventoryFixture(
        PrepareInventoryFixtureCommand command) {
        EnsureServerFixtureRole("Inventory fixtures");
        if (command.PlayerItemCount is < 1 or > 64
            || command.ChestItemCount is < 0 or > 64) {
            throw new AutomationCommandException(
                "invalid_payload",
                "Inventory fixture counts must be between 0 and 64, with a non-empty player stack.");
        }
        EnsureNoTypedPlayerActionInProgress();
        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        SubsystemPlayers players = project.FindSubsystem<SubsystemPlayers>(true);
        SubsystemTerrain terrain = project.FindSubsystem<SubsystemTerrain>(true);
        SubsystemBlockEntities blockEntities = project.FindSubsystem<SubsystemBlockEntities>(true);
        ComponentPlayer actor = ResolvePlayer(players, command.PlayerIndex, requireMainPlayer: false);
        if (actor.PlayerData.IsMainPlayer) {
            throw new AutomationCommandException(
                "invalid_actor",
                "The inventory fixture expects a connected remote player.");
        }

        Vector3 origin = actor.ComponentBody.Position;
        Point3 chestPoint = new(
            (int)MathF.Floor(origin.X) + 3,
            Math.Clamp((int)MathF.Floor(origin.Y), 2, 252),
            (int)MathF.Floor(origin.Z));
        terrain.ChangeCell(
            chestPoint.X,
            chestPoint.Y,
            chestPoint.Z,
            Terrain.MakeBlockValue(ChestBlock.Index));
        terrain.TerrainUpdater.RequestSynchronousUpdate();
        ComponentChest chest = blockEntities.GetBlockEntity(chestPoint)?.Entity
            .FindComponent<ComponentChest>()
            ?? throw new AutomationCommandException(
                "fixture_failed",
                "Chest block entity was not created synchronously.");
        IInventory playerInventory = actor.ComponentMiner.Inventory
            ?? throw new AutomationCommandException(
                "invalid_state",
                "Player inventory is unavailable.");
        int stackValue = Terrain.MakeBlockValue(CoalChunkBlock.Index);
        int dropValue = Terrain.MakeBlockValue(DirtBlock.Index);
        ClearInventory(playerInventory);
        ClearInventory(chest);
        SetInventorySlot(playerInventory, 0, stackValue, command.PlayerItemCount);
        SetInventorySlot(playerInventory, 1, dropValue, 3);
        SetInventorySlot(chest, 0, stackValue, command.ChestItemCount);
        playerInventory.ActiveSlotIndex = 0;

        Vector3 playerPosition = new(
            chestPoint.X + 0.5f,
            chestPoint.Y + 0.05f,
            chestPoint.Z - 2f);
        PrepareInventoryActor(actor, playerPosition);
        QueueFixtureSynchronization(new PlayerBodyUpdatePacket(actor, forceFull: true));
        return new InventoryFixture(
            command.PlayerIndex,
            actor.Entity.Id,
            playerInventory.Id,
            chest.Entity.Id,
            chest.Id,
            new TerrainCellCoordinate(chestPoint.X, chestPoint.Y, chestPoint.Z),
            ToVector(playerPosition),
            stackValue,
            dropValue,
            command.PlayerItemCount,
            command.ChestItemCount);
    }

    private ControlAck ApplyInventoryFixture(ApplyInventoryFixtureCommand command) {
        InventoryFixture fixture = command.Fixture
            ?? throw new AutomationCommandException("invalid_payload", "Fixture payload is required.");
        EnsureClientActionRole("Inventory actor fixtures");
        EnsureNoTypedPlayerActionInProgress();
        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        ComponentPlayer actor = ResolvePlayer(
            project.FindSubsystem<SubsystemPlayers>(true),
            fixture.PlayerIndex,
            requireMainPlayer: true);
        if (actor.Entity.Id != fixture.PlayerEntityId) {
            throw new AutomationCommandException(
                "fixture_mismatch",
                "The inventory fixture player entity does not match this client.");
        }
        PrepareInventoryActor(actor, ToVector(fixture.PlayerPosition));
        QueueFixtureSynchronization(new PlayerBodyUpdatePacket(actor, forceFull: true));
        return new ControlAck(true, $"Applied inventory fixture for player {fixture.PlayerIndex}.");
    }

    private ActionReceipt QueueInventoryMove(
        AutomationCommandContext context,
        InventoryMoveCommand command) {
        EnsureClientActionRole("Inventory move actions");
        EnsureNoTypedPlayerActionInProgress();
        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        if (command.ExpectRejected) {
            if (command.PlayerIndex == NetworkManager.PlayerIndex) {
                throw new AutomationCommandException(
                    "invalid_payload",
                    "An expected-rejection move must target another player's inventory.");
            }
        }
        else {
            ResolvePlayer(
                project.FindSubsystem<SubsystemPlayers>(true),
                command.PlayerIndex,
                requireMainPlayer: true);
        }
        SubsystemInventories inventories = project.FindSubsystem<SubsystemInventories>(true);
        IInventory from = inventories.GetInventoryById(command.FromInventoryId);
        IInventory to = inventories.GetInventoryById(command.ToInventoryId);
        if (from == null || to == null) {
            throw new AutomationCommandException(
                "inventory_not_found",
                "Move source or destination inventory is unavailable on this client.");
        }
        if (!InventoryPacketHelpers.IsSlotIndexValid(from, command.FromSlotIndex)
            || !InventoryPacketHelpers.IsSlotIndexValid(to, command.ToSlotIndex)
            || command.Count <= 0) {
            throw new AutomationCommandException("invalid_payload", "Move slot or count is invalid.");
        }

        int sourceCount = from.GetSlotCount(command.FromSlotIndex);
        int sourceValue = from.GetSlotValue(command.FromSlotIndex);
        int targetCount = to.GetSlotCount(command.ToSlotIndex);
        int targetValue = to.GetSlotValue(command.ToSlotIndex);
        int capacity = to.GetSlotCapacity(command.ToSlotIndex, sourceValue);
        int moved = targetCount == 0 || targetValue == sourceValue
            ? Math.Max(0, MathUtils.Min(command.Count, sourceCount, capacity - targetCount))
            : 0;
        if (!command.ExpectRejected && moved <= 0) {
            throw new AutomationCommandException(
                "move_not_possible",
                "The requested move cannot change the selected inventories.");
        }
        long tick = Time.FrameIndex;
        RecordInventoryAction(
            context.RequestId,
            "move",
            ActionEventPhase.Queued,
            command.PlayerIndex,
            $"{command.FromInventoryId}:{command.FromSlotIndex}->{command.ToInventoryId}:{command.ToSlotIndex}",
            command.ExpectRejected ? "queued_expected_rejection" : "queued");
        MoveItemPacket packet = new(
            from,
            command.FromSlotIndex,
            to,
            command.ToSlotIndex,
            command.Count);
        if (command.ExpectRejected) {
            QueueFaultInjection(packet);
        }
        else {
            QueueProductionRequest(packet);
        }
        if (command.ExpectRejected) {
            RecordInventoryAction(
                context.RequestId,
                "move",
                ActionEventPhase.Observed,
                command.PlayerIndex,
                command.FromInventoryId.ToString(CultureInfo.InvariantCulture),
                "request_sent_expected_rejection");
        }
        else {
            pendingInventoryAction = new PendingInventoryAction(
                context.RequestId,
                InventoryActionKind.Move,
                command.PlayerIndex,
                command.FromInventoryId,
                command.FromSlotIndex,
                sourceCount - moved,
                command.ToInventoryId,
                command.ToSlotIndex,
                targetCount + moved,
                -1,
                -1,
                checked(Time.FrameIndex + InventoryNetworkObservationDeadlineFrames));
            ScheduleInventoryActionTimeout(pendingInventoryAction);
        }
        (ActionProvenance provenance, string entry) = InventoryEvidence(
            "move",
            command.ExpectRejected ? "queued_expected_rejection" : "queued");
        return new ActionReceipt(
            context.RequestId,
            true,
            tick,
            command.PlayerIndex,
            "queued",
            Provenance: provenance,
            ProductionEntry: entry);
    }

    private ActionReceipt QueueInventorySelectSlot(
        AutomationCommandContext context,
        InventorySelectSlotCommand command) {
        EnsureClientActionRole("Inventory slot selection actions");
        EnsureNoTypedPlayerActionInProgress();
        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        ComponentPlayer actor = ResolvePlayer(
            project.FindSubsystem<SubsystemPlayers>(true),
            command.PlayerIndex,
            requireMainPlayer: true);
        IInventory inventory = project.FindSubsystem<SubsystemInventories>(true)
            .GetInventoryById(command.InventoryId);
        if (inventory == null
            || inventory != actor.ComponentMiner.Inventory
            || !InventoryPacketHelpers.IsActiveSlotIndexValid(inventory, command.SlotIndex)) {
            throw new AutomationCommandException(
                "invalid_payload",
                "Only a valid slot in the actor's own inventory can be selected.");
        }
        long tick = Time.FrameIndex;
        RecordInventoryAction(
            context.RequestId,
            "selectSlot",
            ActionEventPhase.Queued,
            command.PlayerIndex,
            $"{command.InventoryId}:{command.SlotIndex}",
            "queued");
        pendingInventoryAction = new PendingInventoryAction(
            context.RequestId,
            InventoryActionKind.SelectSlot,
            command.PlayerIndex,
            command.InventoryId,
            command.SlotIndex,
            -1,
            0,
            0,
            -1,
            command.SlotIndex,
            NetworkStateRevisionTracker.GetAppliedRevision(
                inventory.Project,
                NetworkStateRevisionDomain.Inventory,
                inventory.Id) + 1,
            checked(Time.FrameIndex + InventoryNetworkObservationDeadlineFrames));
        inventory.ActiveSlotIndex = command.SlotIndex;
        ScheduleSelectSlotObservation(pendingInventoryAction);
        ScheduleInventoryActionTimeout(pendingInventoryAction);
        return new ActionReceipt(
            context.RequestId,
            true,
            tick,
            command.PlayerIndex,
            "queued",
            Provenance: ActionProvenance.ProductionInput,
            ProductionEntry: ProductionPathEntries.InventorySelectSlot);
    }

    private ActionReceipt QueueInventoryDrop(
        AutomationCommandContext context,
        InventoryDropCommand command) {
        EnsureClientActionRole("Inventory drop actions");
        EnsureNoTypedPlayerActionInProgress();
        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        ComponentPlayer actor = ResolvePlayer(
            project.FindSubsystem<SubsystemPlayers>(true),
            command.PlayerIndex,
            requireMainPlayer: true);
        IInventory inventory = actor.ComponentMiner.Inventory;
        int slot = inventory.ActiveSlotIndex;
        if (!InventoryPacketHelpers.IsSlotIndexValid(inventory, slot)
            || inventory.GetSlotCount(slot) <= 0) {
            throw new AutomationCommandException(
                "empty_slot",
                "The actor's active inventory slot is empty.");
        }
        long tick = Time.FrameIndex;
        RecordInventoryAction(
            context.RequestId,
            "drop",
            ActionEventPhase.Queued,
            command.PlayerIndex,
            $"{inventory.Id}:{slot}",
            "queued");
        pendingInventoryAction = new PendingInventoryAction(
            context.RequestId,
            InventoryActionKind.Drop,
            command.PlayerIndex,
            inventory.Id,
            slot,
            0,
            0,
            0,
            -1,
            -1,
            -1,
            checked(Time.FrameIndex + InventoryNetworkObservationDeadlineFrames));
        InputHijackState.QueueDrop();
        ScheduleInventoryActionTimeout(pendingInventoryAction);
        return new ActionReceipt(
            context.RequestId,
            true,
            tick,
            command.PlayerIndex,
            "queued",
            Provenance: ActionProvenance.ProductionInput,
            ProductionEntry: ProductionPathEntries.InventoryDrop);
    }

    private InventoryRevisionBurstResult RunInventoryRevisionBurst(
        AutomationCommandContext context,
        RunInventoryRevisionBurstCommand command) {
        EnsureServerFixtureRole("Inventory revision bursts");
        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        IInventory inventory = project.FindSubsystem<SubsystemInventories>(true)
            .GetInventoryById(command.InventoryId);
        if (inventory == null
            || !InventoryPacketHelpers.IsSlotIndexValid(inventory, command.SlotIndex)
            || command.IntermediateCount is < 0 or > 64
            || command.FinalCount is < 0 or > 64) {
            throw new AutomationCommandException("invalid_payload", "Revision burst inventory data is invalid.");
        }
        int value = inventory.GetSlotValue(command.SlotIndex);
        if (value == 0) {
            value = Terrain.MakeBlockValue(CoalChunkBlock.Index);
        }
        InventorySyncPacket stale = new(inventory);
        long staleRevision = stale.m_revision;
        RecordInventoryAction(
            context.RequestId,
            "revisionBurst",
            ActionEventPhase.Queued,
            -1,
            $"{inventory.Id}:{command.SlotIndex}",
            "queued");
        SetInventorySlot(inventory, command.SlotIndex, value, command.IntermediateCount);
        SetInventorySlot(inventory, command.SlotIndex, value, command.FinalCount);
        long finalRevision = NetworkStateRevisionTracker.GetAuthoritativeRevision(
            project,
            NetworkStateRevisionDomain.Inventory,
            inventory.Id);
        if (command.SendStaleSnapshot) {
            QueueFaultInjectionAfterSettle(stale);
        }
        RecordInventoryAction(
            context.RequestId,
            "revisionBurst",
            ActionEventPhase.Applied,
            -1,
            inventory.Id.ToString(CultureInfo.InvariantCulture),
            "final_state_queued");
        ActionReceipt receipt = new(
            context.RequestId,
            true,
            Time.FrameIndex,
            -1,
            command.SendStaleSnapshot ? "stale_snapshot_queued" : "applied",
            Provenance: ActionProvenance.FaultInjection,
            ProductionEntry: ProductionPathEntries.InventoryRevision);
        return new InventoryRevisionBurstResult(
            receipt,
            inventory.Id,
            command.SlotIndex,
            value,
            command.FinalCount,
            staleRevision,
            finalRevision);
    }

    private void HandleTypedInventorySlotChanged(IInventory inventory, int slotIndex) {
        PendingInventoryAction? pending = pendingInventoryAction;
        if (pending == null
            || pending.Kind == InventoryActionKind.SelectSlot
            || NetworkManager.IsServerRunning) {
            return;
        }
        if (!IsInventoryActionSatisfied(pending)) {
            return;
        }
        CompleteInventoryAction(pending, "authoritative_inventory_observed");
    }

    private void HandleTypedActiveSlotChanged(ComponentPlayer player) {
        PendingInventoryAction? pending = pendingInventoryAction;
        if (pending?.Kind != InventoryActionKind.SelectSlot
            || player.PlayerData.PlayerIndex != pending.PlayerIndex
            || player.ComponentMiner.Inventory.Id != pending.PrimaryInventoryId
            || player.ComponentMiner.Inventory.ActiveSlotIndex != pending.ExpectedActiveSlot
            || NetworkStateRevisionTracker.GetAppliedRevision(
                player.Entity.Project,
                NetworkStateRevisionDomain.Inventory,
                pending.PrimaryInventoryId) < pending.MinimumRevision) {
            return;
        }
        CompleteInventoryAction(pending, "production_active_slot_observed");
    }

    private void HandleTypedDropEvent(ComponentPlayer player) {
        PendingInventoryAction? pending = pendingInventoryAction;
        if (pending?.Kind == InventoryActionKind.Drop
            && player.PlayerData.PlayerIndex == pending.PlayerIndex) {
            RecordInventoryAction(
                pending.ActionId,
                "drop",
                ActionEventPhase.Observed,
                pending.PlayerIndex,
                pending.PrimaryInventoryId.ToString(CultureInfo.InvariantCulture),
                "production_drop_request_observed");
        }
    }

    private bool IsInventoryActionSatisfied(PendingInventoryAction pending) {
        Project? project = GameManager.Project;
        SubsystemInventories? inventories = project?.FindSubsystem<SubsystemInventories>();
        IInventory? primary = inventories?.GetInventoryById(pending.PrimaryInventoryId);
        if (primary == null) {
            return false;
        }
        if (pending.Kind == InventoryActionKind.SelectSlot) {
            return primary.ActiveSlotIndex == pending.ExpectedActiveSlot
                && NetworkStateRevisionTracker.GetAppliedRevision(
                    primary.Project,
                    NetworkStateRevisionDomain.Inventory,
                    primary.Id) >= pending.MinimumRevision;
        }
        if (primary.GetSlotCount(pending.PrimarySlotIndex) != pending.ExpectedPrimaryCount) {
            return false;
        }
        if (pending.SecondaryInventoryId <= 0) {
            return true;
        }
        IInventory? secondary = inventories?.GetInventoryById(pending.SecondaryInventoryId);
        return secondary != null
            && secondary.GetSlotCount(pending.SecondarySlotIndex) == pending.ExpectedSecondaryCount;
    }

    private void ScheduleSelectSlotObservation(PendingInventoryAction pending) {
        Time.QueueFrameIndexDelayedExecution(checked(Time.FrameIndex + 1), () => {
            if (pendingInventoryAction?.ActionId != pending.ActionId
                || pending.Kind != InventoryActionKind.SelectSlot) {
                return;
            }
            if (IsInventoryActionSatisfied(pending)) {
                CompleteInventoryAction(pending, "authoritative_inventory_observed");
                return;
            }
            if (Time.FrameIndex < pending.DeadlineFrame) {
                ScheduleSelectSlotObservation(pending);
            }
        });
    }

    private void ScheduleInventoryActionTimeout(PendingInventoryAction pending) {
        Time.QueueFrameIndexDelayedExecution(pending.DeadlineFrame, () => {
            if (pendingInventoryAction?.ActionId != pending.ActionId) {
                return;
            }
            RecordInventoryAction(
                pending.ActionId,
                pending.Kind.ToString(),
                ActionEventPhase.Rejected,
                pending.PlayerIndex,
                pending.PrimaryInventoryId.ToString(CultureInfo.InvariantCulture),
                "authoritative_state_not_observed");
            pendingInventoryAction = null;
        });
    }

    private void CompleteInventoryAction(PendingInventoryAction pending, string code) {
        RecordInventoryAction(
            pending.ActionId,
            pending.Kind switch {
                InventoryActionKind.Move => "move",
                InventoryActionKind.SelectSlot => "selectSlot",
                InventoryActionKind.Drop => "drop",
                _ => "inventoryAction"
            },
            ActionEventPhase.Applied,
            pending.PlayerIndex,
            pending.PrimaryInventoryId.ToString(CultureInfo.InvariantCulture),
            code);
        pendingInventoryAction = null;
    }

    private void RecordInventoryAction(
        string actionId,
        string name,
        ActionEventPhase phase,
        int playerIndex,
        string target,
        string code) {
        (ActionProvenance provenance, string entry) = InventoryEvidence(name, code);
        actionEventJournal.Record(
            Time.FrameIndex,
            actionId,
            nodeRole,
            AutomationCommandDomain.Inventory,
            name,
            phase,
            playerIndex >= 0 ? playerIndex : null,
            target,
            code,
            provenance: provenance,
            productionEntry: entry);
    }

    private static (ActionProvenance Provenance, string Entry) InventoryEvidence(
        string name,
        string code) {
        if (string.Equals(name, "revisionBurst", StringComparison.Ordinal)) {
            return (ActionProvenance.FaultInjection, ProductionPathEntries.InventoryRevision);
        }
        if (string.Equals(name, "selectSlot", StringComparison.OrdinalIgnoreCase)) {
            return (ActionProvenance.ProductionInput, ProductionPathEntries.InventorySelectSlot);
        }
        if (string.Equals(name, "drop", StringComparison.OrdinalIgnoreCase)) {
            return (ActionProvenance.ProductionInput, ProductionPathEntries.InventoryDrop);
        }
        bool fault = code.Contains("expected_rejection", StringComparison.Ordinal)
            || code.Contains("rejection_probe", StringComparison.Ordinal);
        return fault
            ? (ActionProvenance.FaultInjection, ProductionPathEntries.InventoryMove)
            : (ActionProvenance.ProductionRequest, ProductionPathEntries.InventoryMove);
    }

    private static void EnsureServerFixtureRole(string operation) {
        if (NetworkManager.IsClientRunning || !NetworkManager.IsServerRunning) {
            throw new AutomationCommandException(
                "permission_denied",
                $"{operation} must run on the server node.");
        }
    }

    private void EnsureClientActionRole(string operation) {
        if (!nodeRole.StartsWith("client", StringComparison.Ordinal)
            || !NetworkManager.IsClientRunning) {
            throw new AutomationCommandException(
                "permission_denied",
                $"{operation} must originate from a connected client node.");
        }
    }

    private static void PrepareInventoryActor(ComponentPlayer actor, Vector3 position) {
        ComponentBody body = actor.ComponentBody;
        body.Position = position;
        body.Rotation = Quaternion.Identity;
        body.Velocity = Vector3.Zero;
        body.IsGravityEnabled = false;
        body.IsSmoothRiseEnabled = false;
        actor.ComponentLocomotion.IsCreativeFlyEnabled = true;
        actor.m_lastActionTime = -1000;
        InputHijackState.ResetGameplayInputs();
    }

    private static void ClearInventory(IInventory inventory) {
        for (int index = 0; index < inventory.SlotsCount; index++) {
            SetInventorySlot(inventory, index, 0, 0);
        }
    }

    private static void SetInventorySlot(
        IInventory inventory,
        int slotIndex,
        int value,
        int count) {
        if (inventory.GetSlotCount(slotIndex) == count
            && (count == 0 || inventory.GetSlotValue(slotIndex) == value)) {
            return;
        }
        inventory.SetSlotValue(
            slotIndex,
            new ComponentInventoryBase.Slot { Value = value, Count = count });
    }
}
