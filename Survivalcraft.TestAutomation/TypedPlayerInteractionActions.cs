using System.Globalization;
using Engine;
using Game;
using Game.Network;
using GameEntitySystem;
using Survivalcraft.Testing.Control;
using Survivalcraft.Testing.Snapshots;

namespace Survivalcraft.TestAutomation;

public partial class TestAutomationModLoader {
    private PendingTerrainAction? pendingTerrainAction;
    private bool typedPlayerActionsInitialized;

    private sealed record PendingTerrainAction(
        string ActionId,
        PlayerTerrainActionKind Kind,
        int PlayerIndex,
        TerrainCellCoordinate TargetCell);

    private void InitializeTypedPlayerActions() {
        if (typedPlayerActionsInitialized) {
            return;
        }
        string[] hookNames = [
            "OnNetworkPlayerInteract",
            "OnNetworkPlayerDig",
            "OnNetworkPlayerBodyUpdate",
            "OnNetworkActiveSlotChanged",
            "OnNetworkPlayerDrop",
            "OnNetworkPlayerHit",
            "OnNetworkInventorySlotChanged",
            "OnNetworkPlayerRestart"
        ];
        foreach (string hookName in hookNames) {
            ModsManager.RegisterHook(hookName, this);
        }
        typedPlayerActionsInitialized = true;
    }

    private void DisposeTypedPlayerActions() {
        if (!typedPlayerActionsInitialized) {
            return;
        }
        typedPlayerActionsInitialized = false;
        ResetTypedPlayerActions();
    }

    public override void OnNetworkPlayerInteract(ComponentPlayer player) =>
        HandleTypedInteractEvent(player);

    public override void OnNetworkPlayerDig(ComponentPlayer player) =>
        HandleTypedDigEvent(player);

    public override void OnNetworkPlayerBodyUpdate(ComponentPlayer player, bool isMainPlayer) =>
        HandleTypedBodyUpdate(player, isMainPlayer);

    public override void OnNetworkActiveSlotChanged(ComponentPlayer player) =>
        HandleTypedActiveSlotChanged(player);

    public override void OnNetworkPlayerDrop(ComponentPlayer player) =>
        HandleTypedDropEvent(player);

    public override void OnNetworkPlayerHit(
        ComponentPlayer player,
        ComponentBody hitBody,
        Vector3 hitPoint,
        Vector3 direction) =>
        HandleTypedHitEvent(player, hitBody, hitPoint, direction);

    public override void OnNetworkInventorySlotChanged(IInventory inventory, int slotIndex) =>
        HandleTypedInventorySlotChanged(inventory, slotIndex);

    public override void OnNetworkPlayerRestart(PlayerData playerData) =>
        HandleTypedPlayerRestart(playerData);

    private void ResetTypedPlayerActions() {
        pendingTerrainAction = null;
        pendingPlayerMotionAction = null;
        pendingTerrainStreamingMoveTrigger = null;
        pendingInventoryAction = null;
        pendingHealthAction = null;
        ResetEntityLifecycleFixtures();
        ResetMountFixtures();
        ResetLogicFixtures();
        InputHijackState.ResetGameplayInputs();
    }

    private TerrainInteractionFixture PrepareTerrainInteractionFixture(
        PrepareTerrainInteractionFixtureCommand command) {
        if (nodeRole != "server" || !NetworkManager.IsServerRunning) {
            throw new AutomationCommandException(
                "permission_denied",
                "Terrain interaction fixtures must be prepared by the server node.");
        }
        if (!Enum.IsDefined(command.Kind)) {
            throw new AutomationCommandException("invalid_payload", "Unknown terrain interaction kind.");
        }
        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        SubsystemPlayers players = project.FindSubsystem<SubsystemPlayers>(true);
        SubsystemTerrain terrain = project.FindSubsystem<SubsystemTerrain>(true);
        ComponentPlayer actor = ResolvePlayer(players, command.PlayerIndex, requireMainPlayer: false);
        if (actor.PlayerData.IsMainPlayer) {
            throw new AutomationCommandException(
                "invalid_actor",
                "The server fixture expects a connected remote player.");
        }

        TerrainCellCoordinate target = command.TargetCell
            ?? CreateDefaultTarget(players, command.Kind);
        ValidateTerrainCoordinate(target);
        TerrainCellCoordinate expectedCell = command.Kind == PlayerTerrainActionKind.Place
            ? new TerrainCellCoordinate(target.X, target.Y, target.Z + 1)
            : target;
        int activeValue;
        int expectedValue;
        switch (command.Kind) {
            case PlayerTerrainActionKind.Use:
                terrain.ChangeCell(
                    target.X,
                    target.Y,
                    target.Z,
                    Terrain.MakeBlockValue(
                        PlanksBlock.Index,
                        0,
                        PaintedCubeBlock.SetColor(0, null)));
                activeValue = Terrain.MakeBlockValue(
                    PaintBucketBlock.Index,
                    0,
                    PaintBucketBlock.SetColor(0, 4));
                expectedValue = Terrain.MakeBlockValue(
                    PlanksBlock.Index,
                    0,
                    PaintedCubeBlock.SetColor(0, 4));
                break;
            case PlayerTerrainActionKind.Place:
                terrain.ChangeCell(
                    target.X,
                    target.Y,
                    target.Z,
                    Terrain.MakeBlockValue(GraniteBlock.Index));
                terrain.ChangeCell(expectedCell.X, expectedCell.Y, expectedCell.Z, 0);
                activeValue = Terrain.MakeBlockValue(PlanksBlock.Index);
                expectedValue = activeValue;
                break;
            case PlayerTerrainActionKind.Dig:
                terrain.ChangeCell(
                    target.X,
                    target.Y,
                    target.Z,
                    Terrain.MakeBlockValue(DirtBlock.Index));
                activeValue = Terrain.MakeBlockValue(DiamondShovelBlock.Index);
                expectedValue = 0;
                break;
            default:
                throw new AutomationCommandException("invalid_payload", "Unknown terrain interaction kind.");
        }

        ClearInteractionPath(terrain, target, expectedCell);
        int chunkMinX = (target.X >> 4) << 4;
        int chunkMinZ = (target.Z >> 4) << 4;
        Vector3 playerPosition = new(
            chunkMinX + 8.5f,
            target.Y + 1f,
            chunkMinZ + 11f);
        PreparePlayerForFixture(actor, activeValue, playerPosition);
        terrain.TerrainUpdater.RequestSynchronousUpdate();
        int initialValue = Terrain.ReplaceLight(
            terrain.Terrain.GetCellValue(
                expectedCell.X,
                expectedCell.Y,
                expectedCell.Z),
            0);
        int targetValue = Terrain.ReplaceLight(
            terrain.Terrain.GetCellValue(
                target.X,
                target.Y,
                target.Z),
            0);
        return new TerrainInteractionFixture(
            command.Kind,
            command.PlayerIndex,
            target,
            expectedCell,
            targetValue,
            initialValue,
            expectedValue,
            activeValue,
            1,
            ToVector(playerPosition));
    }

    private ControlAck ApplyTerrainInteractionFixture(
        ApplyTerrainInteractionFixtureCommand command) {
        TerrainInteractionFixture fixture = command.Fixture
            ?? throw new AutomationCommandException("invalid_payload", "Fixture payload is required.");
        if (!nodeRole.StartsWith("client", StringComparison.Ordinal)
            || !NetworkManager.IsClientRunning) {
            throw new AutomationCommandException(
                "permission_denied",
                "Terrain interaction actor fixtures must be applied by a client node.");
        }
        if (!Enum.IsDefined(fixture.Kind)
            || fixture.ActiveCount is < 1 or > 999
            || fixture.ActiveValue < 0) {
            throw new AutomationCommandException("invalid_payload", "Fixture item data is invalid.");
        }
        ValidateTerrainCoordinate(fixture.TargetCell);
        ValidateTerrainCoordinate(fixture.ExpectedCell);
        Vector3 position = ToVector(fixture.PlayerPosition);
        if (!IsFinite(position)) {
            throw new AutomationCommandException("invalid_payload", "Fixture player position is invalid.");
        }

        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        SubsystemPlayers players = project.FindSubsystem<SubsystemPlayers>(true);
        ComponentPlayer actor = ResolvePlayer(players, fixture.PlayerIndex, requireMainPlayer: true);
        PreparePlayerForFixture(actor, fixture.ActiveValue, position, fixture.ActiveCount);
        NetworkManager.Queue(new PlayerBodyUpdatePacket(actor, forceFull: true));
        return new ControlAck(
            true,
            $"Prepared and synchronized player {fixture.PlayerIndex} for {fixture.Kind} "
            + $"at {FormatCell(fixture.TargetCell)}.");
    }

    private ActionReceipt QueuePlayerUse(
        AutomationCommandContext context,
        PlayerUseCommand command) =>
        QueueTerrainAction(
            context,
            PlayerTerrainActionKind.Use,
            command.PlayerIndex,
            command.TargetCell,
            holdFrames: 1);

    private ActionReceipt QueuePlayerPlace(
        AutomationCommandContext context,
        PlayerPlaceCommand command) =>
        QueueTerrainAction(
            context,
            PlayerTerrainActionKind.Place,
            command.PlayerIndex,
            command.TargetCell,
            holdFrames: 1);

    private ActionReceipt QueuePlayerDig(
        AutomationCommandContext context,
        PlayerDigCommand command) {
        if (command.HoldFrames is < 1 or > 1200) {
            throw new AutomationCommandException(
                "invalid_payload",
                "Dig holdFrames must be between 1 and 1200.");
        }
        return QueueTerrainAction(
            context,
            PlayerTerrainActionKind.Dig,
            command.PlayerIndex,
            command.TargetCell,
            command.HoldFrames);
    }

    private ActionReceipt QueueTerrainAction(
        AutomationCommandContext context,
        PlayerTerrainActionKind kind,
        int playerIndex,
        TerrainCellCoordinate targetCell,
        int holdFrames) {
        if (!nodeRole.StartsWith("client", StringComparison.Ordinal)
            || !NetworkManager.IsClientRunning) {
            throw new AutomationCommandException(
                "permission_denied",
                "Player terrain actions must originate from a connected client node.");
        }
        if (pendingTerrainAction != null) {
            throw new AutomationCommandException(
                "action_in_progress",
                $"Action '{pendingTerrainAction.ActionId}' is still in progress.");
        }
        if (pendingPlayerMotionAction != null) {
            throw new AutomationCommandException(
                "action_in_progress",
                $"Action '{pendingPlayerMotionAction.ActionId}' is still in progress.");
        }
        ValidateTerrainCoordinate(targetCell);
        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        SubsystemPlayers players = project.FindSubsystem<SubsystemPlayers>(true);
        ComponentPlayer actor = ResolvePlayer(players, playerIndex, requireMainPlayer: true);
        RaycastMode raycastMode = kind == PlayerTerrainActionKind.Dig
            ? RaycastMode.Digging
            : RaycastMode.Interaction;
        Ray3 ray = CreateValidatedActionRay(actor, targetCell, raycastMode);

        long tick = Time.FrameIndex;
        PendingTerrainAction pending = new(context.RequestId, kind, playerIndex, targetCell);
        pendingTerrainAction = pending;
        actionEventJournal.Record(
            tick,
            context.RequestId,
            nodeRole,
            AutomationCommandDomain.Player,
            EventName(kind),
            ActionEventPhase.Queued,
            playerIndex,
            FormatCell(targetCell),
            "queued",
            ActionData(pending, "queued"));
        if (kind == PlayerTerrainActionKind.Dig) {
            InputHijackState.HoldDigRay(ray, holdFrames);
        }
        else {
            InputHijackState.QueueInteractRay(ray);
        }
        return new ActionReceipt(
            context.RequestId,
            true,
            tick,
            playerIndex,
            "queued");
    }

    private void HandleTypedInteractEvent(ComponentPlayer player) {
        PendingTerrainAction? pending = pendingTerrainAction;
        if (pending == null
            || pending.Kind == PlayerTerrainActionKind.Dig
            || player.PlayerData.PlayerIndex != pending.PlayerIndex
            || !player.PlayerData.IsMainPlayer) {
            return;
        }
        ComponentPlayer.InteractEvent actual = player.interactEvent.interactEvent;
        if (actual == ComponentPlayer.InteractEvent.None) {
            return;
        }
        ComponentPlayer.InteractEvent expected = pending.Kind == PlayerTerrainActionKind.Place
            ? ComponentPlayer.InteractEvent.Place
            : ComponentPlayer.InteractEvent.Start;
        bool matches = actual == expected;
        actionEventJournal.Record(
            Time.FrameIndex,
            pending.ActionId,
            nodeRole,
            AutomationCommandDomain.Player,
            EventName(pending.Kind),
            matches ? ActionEventPhase.Applied : ActionEventPhase.Rejected,
            pending.PlayerIndex,
            FormatCell(pending.TargetCell),
            matches ? "input_applied" : "unexpected_input_event",
            ActionData(pending, actual.ToString()));
        pendingTerrainAction = null;
    }

    private void HandleTypedDigEvent(ComponentPlayer player) {
        PendingTerrainAction? pending = pendingTerrainAction;
        if (pending?.Kind != PlayerTerrainActionKind.Dig
            || player.PlayerData.PlayerIndex != pending.PlayerIndex
            || !player.PlayerData.IsMainPlayer) {
            return;
        }
        ComponentPlayer.DigEvent actual = player.digEvent.digEvent;
        if (actual == ComponentPlayer.DigEvent.Start) {
            actionEventJournal.Record(
                Time.FrameIndex,
                pending.ActionId,
                nodeRole,
                AutomationCommandDomain.Player,
                EventName(pending.Kind),
                ActionEventPhase.Observed,
                pending.PlayerIndex,
                FormatCell(pending.TargetCell),
                "dig_started",
                ActionData(pending, actual.ToString()));
            return;
        }
        if (actual is not (ComponentPlayer.DigEvent.End or ComponentPlayer.DigEvent.Cancel)) {
            return;
        }
        bool applied = actual == ComponentPlayer.DigEvent.End;
        actionEventJournal.Record(
            Time.FrameIndex,
            pending.ActionId,
            nodeRole,
            AutomationCommandDomain.Player,
            EventName(pending.Kind),
            applied ? ActionEventPhase.Applied : ActionEventPhase.Rejected,
            pending.PlayerIndex,
            FormatCell(pending.TargetCell),
            applied ? "input_applied" : "input_cancelled",
            ActionData(pending, actual.ToString()));
        InputHijackState.CancelDigRay();
        pendingTerrainAction = null;
    }

    private static ComponentPlayer ResolvePlayer(
        SubsystemPlayers players,
        int playerIndex,
        bool requireMainPlayer,
        bool requireReady = true) {
        PlayerData? player = players.PlayersData.FirstOrDefault(candidate =>
            candidate.PlayerIndex == playerIndex);
        if (player?.ComponentPlayer == null
            || (requireReady && !player.IsReadyForPlaying)) {
            throw new AutomationCommandException(
                "actor_not_found",
                $"Player {playerIndex} is not ready for gameplay.");
        }
        if (requireMainPlayer && !player.IsMainPlayer) {
            throw new AutomationCommandException(
                "permission_denied",
                "A client can only drive its main player.");
        }
        return player.ComponentPlayer;
    }

    private static TerrainCellCoordinate CreateDefaultTarget(
        SubsystemPlayers players,
        PlayerTerrainActionKind kind) {
        Vector3 spawn = players.GlobalSpawnPosition;
        int chunkMinX = ((int)MathF.Floor(spawn.X) >> 4) << 4;
        int chunkMinZ = ((int)MathF.Floor(spawn.Z) >> 4) << 4;
        int inChunkX = kind switch {
            PlayerTerrainActionKind.Use => 4,
            PlayerTerrainActionKind.Place => 8,
            PlayerTerrainActionKind.Dig => 12,
            _ => throw new AutomationCommandException("invalid_payload", "Unknown terrain interaction kind.")
        };
        return new TerrainCellCoordinate(
            chunkMinX + inChunkX,
            (int)MathF.Floor(spawn.Y + 1f),
            chunkMinZ + 8);
    }

    private static void ClearInteractionPath(
        SubsystemTerrain terrain,
        TerrainCellCoordinate target,
        TerrainCellCoordinate expectedCell) {
        terrain.ChangeCell(target.X, target.Y, target.Z + 1, 0);
        for (int z = target.Z + 1; z <= target.Z + 2; z++) {
            if (z != expectedCell.Z || target.Y + 1 != expectedCell.Y) {
                terrain.ChangeCell(target.X, target.Y + 1, z, 0);
            }
        }
        terrain.ChangeCell(target.X, target.Y + 1, target.Z, 0);
    }

    private static void PreparePlayerForFixture(
        ComponentPlayer player,
        int activeValue,
        Vector3 position,
        int activeCount = 1) {
        IInventory inventory = player.ComponentMiner.Inventory
            ?? throw new AutomationCommandException("invalid_state", "Player inventory is unavailable.");
        inventory.ActiveSlotIndex = 0;
        if (inventory.GetSlotValue(0) != activeValue
            || inventory.GetSlotCount(0) != activeCount) {
            inventory.SetSlotValue(
                0,
                new ComponentInventoryBase.Slot { Value = activeValue, Count = activeCount });
        }

        ComponentBody body = player.ComponentBody;
        body.Position = position;
        body.Rotation = Quaternion.Identity;
        body.Velocity = Vector3.Zero;
        body.IsGravityEnabled = false;
        body.IsSmoothRiseEnabled = false;
        player.ComponentLocomotion.IsCreativeFlyEnabled = true;
        player.ComponentMiner.DigCellFace = null;
        player.ComponentMiner.m_digProgress = 0f;
        player.ComponentMiner.ClearAllIsPending();
        player.m_lastActionTime = -1000.0;
        InputHijackState.ResetGameplayInputs();
    }

    private static Ray3 CreateValidatedActionRay(
        ComponentPlayer actor,
        TerrainCellCoordinate targetCell,
        RaycastMode mode) {
        Vector3 eye = actor.ComponentCreatureModel.EyePosition;
        Vector3 target = new(targetCell.X + 0.5f, targetCell.Y + 0.5f, targetCell.Z + 0.5f);
        Vector3 delta = target - eye;
        float distance = delta.Length();
        float reach = actor.m_subsystemGameInfo.WorldSettings.GameMode == GameMode.Creative
            ? SettingsManager.CreativeReach
            : 5f;
        if (!float.IsFinite(distance) || distance < 0.05f || distance > reach + 0.5f) {
            throw new AutomationCommandException(
                "target_not_reachable",
                $"Target {FormatCell(targetCell)} is {distance:R} blocks from the player eye; reach is {reach:R}.");
        }
        Ray3 ray = new(eye, Vector3.Normalize(delta));
        TerrainRaycastResult? hit = actor.ComponentMiner.Raycast<TerrainRaycastResult>(ray, mode);
        if (!hit.HasValue
            || hit.Value.CellFace.X != targetCell.X
            || hit.Value.CellFace.Y != targetCell.Y
            || hit.Value.CellFace.Z != targetCell.Z) {
            string actual = hit.HasValue
                ? FormatCell(new TerrainCellCoordinate(
                    hit.Value.CellFace.X,
                    hit.Value.CellFace.Y,
                    hit.Value.CellFace.Z))
                : "missing";
            throw new AutomationCommandException(
                "target_not_visible",
                $"Action ray did not resolve target {FormatCell(targetCell)}. Actual={actual}.");
        }
        return ray;
    }

    private static void ValidateTerrainCoordinate(TerrainCellCoordinate coordinate) {
        ArgumentNullException.ThrowIfNull(coordinate);
        if (coordinate.Y is < 0 or > 255) {
            throw new AutomationCommandException(
                "invalid_payload",
                "Terrain cell Y coordinates must be between 0 and 255.");
        }
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static Vector3 ToVector(Vector3State value) =>
        new((float)value.X, (float)value.Y, (float)value.Z);

    private static string EventName(PlayerTerrainActionKind kind) =>
        kind switch {
            PlayerTerrainActionKind.Use => "playerUse",
            PlayerTerrainActionKind.Place => "playerPlace",
            PlayerTerrainActionKind.Dig => "playerDig",
            _ => "playerTerrainAction"
        };

    private static string FormatCell(TerrainCellCoordinate cell) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{cell.X},{cell.Y},{cell.Z}");

    private static IReadOnlyDictionary<string, string> ActionData(
        PendingTerrainAction pending,
        string inputEvent) =>
        new Dictionary<string, string> {
            ["kind"] = pending.Kind.ToString(),
            ["target"] = FormatCell(pending.TargetCell),
            ["inputEvent"] = inputEvent
        };
}
