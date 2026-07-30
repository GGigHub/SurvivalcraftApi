using System.Globalization;
using Engine;
using Game;
using Game.Network;
using GameEntitySystem;
using Survivalcraft.Testing.Control;
using Survivalcraft.Testing.Snapshots;

namespace Survivalcraft.TestAutomation;

public partial class TestAutomationModLoader {
    private readonly Dictionary<string, int> pickableFixtureAliases =
        new(StringComparer.Ordinal);
    private Vector3? pickableFixtureOrigin;
    private PendingProjectileAction? pendingProjectileAction;

    private sealed record PendingProjectileAction(
        string ActionId,
        int PlayerIndex,
        int InputEndFrame,
        int DeadlineFrame,
        Vector3 Direction,
        bool InputObserved,
        bool Observed);

    private void ResetPickableProjectileFixtures() {
        pickableFixtureAliases.Clear();
        pickableFixtureOrigin = null;
        pendingProjectileAction = null;
    }

    private PickableProjectileFixture PreparePickableProjectileFixture(
        PreparePickableProjectileFixtureCommand command) {
        EnsurePickableServerRole("Pickable/projectile fixtures");
        ValidateEntityAlias(command.PersistentAlias, nameof(command.PersistentAlias));
        if (command.PersistentCount is < 1 or > 64) {
            throw new AutomationCommandException(
                "invalid_payload",
                "Persistent pickable count must be between 1 and 64.");
        }
        if (pickableFixtureAliases.ContainsKey(command.PersistentAlias)) {
            throw new AutomationCommandException(
                "duplicate_alias",
                $"Pickable alias '{command.PersistentAlias}' already exists.");
        }

        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        SubsystemPlayers players = project.FindSubsystem<SubsystemPlayers>(true);
        SubsystemTerrain terrain = project.FindSubsystem<SubsystemTerrain>(true);
        SubsystemPickables pickables = project.FindSubsystem<SubsystemPickables>(true);
        ComponentPlayer actor = ResolvePlayer(
            players,
            command.ActorPlayerIndex,
            requireMainPlayer: false);
        if (actor.PlayerData.IsMainPlayer) {
            throw new AutomationCommandException(
                "invalid_actor",
                "The pickable/projectile fixture expects a connected remote player.");
        }

        Vector3 spawn = players.GlobalSpawnPosition;
        int chunkMinX = ((int)MathF.Floor(spawn.X) >> 4) << 4;
        int chunkMinZ = ((int)MathF.Floor(spawn.Z) >> 4) << 4;
        int floorY = Math.Clamp((int)MathF.Floor(spawn.Y) - 1, 4, 245);
        Vector3 actorPosition = new(chunkMinX + 4.5f, floorY + 1.05f, chunkMinZ + 8.5f);
        Vector3 persistentPosition = new(chunkMinX + 12.5f, floorY + 1.2f, chunkMinZ + 8.5f);
        BuildPickableProjectileArena(terrain, actorPosition, persistentPosition, floorY);
        terrain.TerrainUpdater.RequestSynchronousUpdate();

        int crossbowIndex = BlocksManager.GetBlockIndex<CrossbowBlock>();
        int crossbowData = CrossbowBlock.SetDraw(0, 15);
        crossbowData = CrossbowBlock.SetArrowType(
            crossbowData,
            ArrowBlock.ArrowType.IronBolt);
        int loadedCrossbowValue = Terrain.MakeBlockValue(crossbowIndex, 0, crossbowData);
        IInventory inventory = actor.ComponentMiner.Inventory
            ?? throw new AutomationCommandException(
                "invalid_state",
                "Player inventory is unavailable.");
        ClearInventory(inventory);
        SetInventorySlot(inventory, 0, loadedCrossbowValue, 1);
        inventory.ActiveSlotIndex = 0;
        PrepareInventoryActor(actor, actorPosition);
        QueueFixtureSynchronization(new PlayerBodyUpdatePacket(actor, forceFull: true));
        QueueFixtureSynchronization(new InventorySyncPacket(inventory));

        int persistentValue = Terrain.MakeBlockValue(DirtBlock.Index);
        Pickable persistent = pickables.AddPickable(
            persistentValue,
            command.PersistentCount,
            persistentPosition,
            Vector3.Zero,
            null,
            actor.Entity)
            ?? throw new AutomationCommandException(
                "fixture_failed",
                "The authoritative pickable subsystem rejected the persistent fixture.");
        persistent.MaxTimeExist = 3600f;
        persistent.Velocity = Vector3.Zero;
        pickableFixtureAliases.Add(command.PersistentAlias, persistent.Id);
        pickableFixtureOrigin = persistentPosition;
        long revision = PickableLifecycleTracker.GetAuthoritativeRevision(project, persistent.Id);
        return new PickableProjectileFixture(
            command.ActorPlayerIndex,
            actor.Entity.Id,
            inventory.Id,
            command.PersistentAlias,
            persistent.Id,
            persistent.Value,
            persistent.Count,
            revision,
            ToVector(persistent.Position),
            ToVector(actorPosition),
            loadedCrossbowValue,
            inventory.GetSlotCount(0));
    }

    private ControlAck ApplyPickableProjectileFixture(
        ApplyPickableProjectileFixtureCommand command) {
        PickableProjectileFixture fixture = command.Fixture
            ?? throw new AutomationCommandException(
                "invalid_payload",
                "Pickable/projectile fixture payload is required.");
        EnsureClientActionRole("Pickable/projectile actor fixtures");
        EnsureNoTypedPlayerActionInProgress();
        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        ComponentPlayer actor = ResolvePlayer(
            project.FindSubsystem<SubsystemPlayers>(true),
            fixture.ActorPlayerIndex,
            requireMainPlayer: true);
        IInventory inventory = actor.ComponentMiner.Inventory
            ?? throw new AutomationCommandException(
                "invalid_state",
                "Player inventory is unavailable.");
        if (actor.Entity.Id != fixture.ActorEntityId
            || inventory.Id != fixture.ActorInventoryId
            || inventory.ActiveSlotIndex != 0
            || inventory.GetSlotValue(0) != fixture.LoadedCrossbowValue
            || inventory.GetSlotCount(0) != fixture.LoadedCrossbowCount) {
            throw new AutomationCommandException(
                "fixture_mismatch",
                "The authoritative loaded-crossbow fixture has not converged on this client. "
                + $"entity={actor.Entity.Id}/{fixture.ActorEntityId}; "
                + $"inventory={inventory.Id}/{fixture.ActorInventoryId}; "
                + $"activeSlot={inventory.ActiveSlotIndex}/0; "
                + $"slot0Value={inventory.GetSlotValue(0)}/{fixture.LoadedCrossbowValue}; "
                + $"slot0Count={inventory.GetSlotCount(0)}/{fixture.LoadedCrossbowCount}.");
        }
        PrepareInventoryActor(actor, ToVector(fixture.ActorPosition));
        PrepareProductionInputCamera(actor);
        QueueFixtureSynchronization(new PlayerBodyUpdatePacket(actor, forceFull: true));
        return new ControlAck(
            true,
            $"Applied pickable/projectile fixture for player {fixture.ActorPlayerIndex}.");
    }

    private ActionReceipt AttractPickable(
        AutomationCommandContext context,
        AttractPickableCommand command) {
        EnsurePickableServerRole("Pickable attraction actions");
        ValidateEntityAlias(command.Alias, nameof(command.Alias));
        Vector3 target = ToVector(command.TargetPosition);
        if (!IsFinite(target)) {
            throw new AutomationCommandException(
                "invalid_payload",
                "Pickable attraction target must be finite.");
        }
        Pickable pickable = ResolvePickableAlias(command.Alias);
        if (Vector3.DistanceSquared(pickable.Position, target) is < 0.25f or > 1_024f) {
            throw new AutomationCommandException(
                "invalid_payload",
                "Pickable attraction target must be between 0.5 and 32 blocks away.");
        }

        IReadOnlyDictionary<string, string> data = new Dictionary<string, string> {
            ["pickableId"] = pickable.Id.ToString(CultureInfo.InvariantCulture),
            ["target"] = FormatVector(target)
        };
        RecordPickableAction(
            context,
            "attract",
            ActionEventPhase.Queued,
            command.Alias,
            "queued",
            data,
            ActionProvenance.AuthoritativeState,
            ProductionPathEntries.PickableAttraction);
        pickable.FlyToPosition = target;
        ModsManager.HookAction(
            "OnNetworkPickableAttract",
            loader => loader.OnNetworkPickableAttract(pickable));
        RecordPickableAction(
            context,
            "attract",
            ActionEventPhase.Applied,
            command.Alias,
            "production_attraction_applied",
            data,
            ActionProvenance.AuthoritativeState,
            ProductionPathEntries.PickableAttraction);
        return new ActionReceipt(
            context.RequestId,
            true,
            Time.FrameIndex,
            -1,
            "applied",
            Provenance: ActionProvenance.AuthoritativeState,
            ProductionEntry: ProductionPathEntries.PickableAttraction);
    }

    private ActionReceipt QueuePlayerFireProjectile(
        AutomationCommandContext context,
        PlayerFireProjectileCommand command) {
        EnsureClientActionRole("Player projectile actions");
        EnsureNoTypedPlayerActionInProgress();
        if (command.HoldFrames is < 2 or > 120) {
            throw new AutomationCommandException(
                "invalid_payload",
                "Projectile aim holdFrames must be between 2 and 120.");
        }
        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        ComponentPlayer actor = ResolvePlayer(
            project.FindSubsystem<SubsystemPlayers>(true),
            command.PlayerIndex,
            requireMainPlayer: true);
        IInventory inventory = actor.ComponentMiner.Inventory
            ?? throw new AutomationCommandException(
                "invalid_state",
                "Player inventory is unavailable.");
        int activeValue = inventory.GetSlotValue(inventory.ActiveSlotIndex);
        if (inventory.GetSlotCount(inventory.ActiveSlotIndex) <= 0
            || BlocksManager.Blocks[Terrain.ExtractContents(activeValue)] is not CrossbowBlock
            || CrossbowBlock.GetDraw(Terrain.ExtractData(activeValue)) != 15
            || !CrossbowBlock.GetArrowType(Terrain.ExtractData(activeValue)).HasValue) {
            throw new AutomationCommandException(
                "fixture_mismatch",
                "The player's active slot is not a loaded, fully drawn crossbow.");
        }

        Camera camera = PrepareProductionInputCamera(actor);
        Vector3 direction = command.Direction == null
            ? camera.ViewDirection
            : ToVector(command.Direction);
        if (!IsFinite(direction) || direction.LengthSquared() <= 0.0001f) {
            throw new AutomationCommandException(
                "invalid_payload",
                "Projectile direction must be finite and non-zero.");
        }
        direction = Vector3.Normalize(direction);
        Vector3 projected = camera.WorldToScreen(
            camera.ViewPosition + direction,
            Matrix.Identity);
        Point2 windowSize = Window.Size;
        if (!IsFinite(projected)
            || projected.X < windowSize.X * 0.02f
            || projected.X >= windowSize.X * 0.98f
            || projected.Y < windowSize.Y * 0.02f
            || projected.Y >= windowSize.Y * 0.98f) {
            throw new AutomationCommandException(
                "production_view_not_ready",
                "The production camera could not project the requested aim ray inside the headless viewport. "
                + $"viewSize={actor.GameWidget.ViewWidget.ActualSize}; "
                + $"windowSize={windowSize}; projected={projected}.");
        }
        int inputEndFrame = checked(Time.FrameIndex + command.HoldFrames);
        pendingProjectileAction = new PendingProjectileAction(
            context.RequestId,
            command.PlayerIndex,
            inputEndFrame,
            checked(inputEndFrame + 120),
            direction,
            false,
            false);
        IReadOnlyDictionary<string, string> data = ProjectileActionData(
            pendingProjectileAction,
            "queued");
        actionEventJournal.Record(
            Time.FrameIndex,
            context.RequestId,
            nodeRole,
            AutomationCommandDomain.Projectile,
            "fireProjectile",
            ActionEventPhase.Queued,
            command.PlayerIndex,
            target: null,
            code: "queued",
            data: data,
            provenance: ActionProvenance.ProductionInput,
            productionEntry: ProductionPathEntries.PlayerProjectileFire);
        InputHijackState.HoldAimRay(
            new Ray3(camera.ViewPosition, direction),
            command.HoldFrames);
        return new ActionReceipt(
            context.RequestId,
            true,
            Time.FrameIndex,
            command.PlayerIndex,
            "queued",
            Provenance: ActionProvenance.ProductionInput,
            ProductionEntry: ProductionPathEntries.PlayerProjectileFire);
    }

    private void HandleTypedAimEvent(ComponentPlayer player) {
        PendingProjectileAction? pending = pendingProjectileAction;
        if (pending == null
            || !player.PlayerData.IsMainPlayer
            || player.PlayerData.PlayerIndex != pending.PlayerIndex) {
            return;
        }
        ComponentPlayer.AimEvent actual = player.aimEvent.aimEvent;
        if (actual == ComponentPlayer.AimEvent.InProgress && !pending.Observed) {
            pending = pending with { Observed = true };
            pendingProjectileAction = pending;
            RecordProjectileEvent(
                pending,
                ActionEventPhase.Observed,
                "production_aim_started",
                actual);
            return;
        }
        if (actual == ComponentPlayer.AimEvent.Complete) {
            RecordProjectileEvent(
                pending,
                ActionEventPhase.Applied,
                "production_aim_completed",
                actual);
            pendingProjectileAction = null;
            return;
        }
        if (actual == ComponentPlayer.AimEvent.Cancel) {
            RecordProjectileEvent(
                pending,
                ActionEventPhase.Rejected,
                "production_aim_cancelled",
                actual);
            pendingProjectileAction = null;
        }
    }

    private void HandleTypedProjectileInputFrame(ComponentPlayer player, bool isMainPlayer) {
        PendingProjectileAction? pending = pendingProjectileAction;
        if (pending == null
            || !isMainPlayer
            || player.PlayerData.PlayerIndex != pending.PlayerIndex) {
            return;
        }

        if (player.ComponentInput.PlayerInput.Aim.HasValue && !pending.InputObserved) {
            pending = pending with { InputObserved = true };
            pendingProjectileAction = pending;
            RecordProjectileEvent(
                pending,
                ActionEventPhase.Observed,
                "component_input_aim_observed",
                player.aimEvent.aimEvent,
                player);
        }

        if (Time.FrameIndex >= pending.DeadlineFrame) {
            RecordProjectileEvent(
                pending,
                ActionEventPhase.Rejected,
                "production_aim_event_not_emitted",
                player.aimEvent.aimEvent,
                player);
            pendingProjectileAction = null;
            InputHijackState.ResetGameplayInputs();
        }
    }

    public override void OnNetworkPlayerAim(ComponentPlayer player) =>
        HandleTypedAimEvent(player);

    private static Camera PrepareProductionInputCamera(ComponentPlayer actor) {
        GameWidget gameWidget = actor.GameWidget;
        Vector2 viewportSize = new(
            Math.Max(Window.Size.X, 1),
            Math.Max(Window.Size.Y, 1));
        if (gameWidget.ViewWidget.ActualSize.X <= 0f
            || gameWidget.ViewWidget.ActualSize.Y <= 0f) {
            // The headless runner intentionally skips drawing. Reproduce the normal
            // widget layout pass so production input validation has a real viewport.
            gameWidget.Measure(viewportSize);
            gameWidget.Arrange(Vector2.Zero, viewportSize);
        }

        Camera camera = gameWidget.ActiveCamera;
        camera.Update(0f);
        // Drawing normally invalidates these matrices every frame. Headless runs do
        // not draw, so invalidate them explicitly after arranging the production view.
        camera.PrepareForDrawing();
        return camera;
    }

    private PickableLifecycleBurstResult RunPickableLifecycleBurst(
        AutomationCommandContext context,
        RunPickableLifecycleBurstCommand command) {
        EnsurePickableServerRole("Pickable lifecycle bursts");
        ValidateEntityAlias(command.TombstoneAlias, nameof(command.TombstoneAlias));
        if (pickableFixtureAliases.ContainsKey(command.TombstoneAlias)) {
            throw new AutomationCommandException(
                "duplicate_alias",
                $"Pickable alias '{command.TombstoneAlias}' already exists.");
        }
        Vector3 origin = pickableFixtureOrigin
            ?? throw new AutomationCommandException(
                "invalid_state",
                "Prepare the pickable/projectile fixture before running a lifecycle burst.");
        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        SubsystemPickables subsystem = project.FindSubsystem<SubsystemPickables>(true);
        int value = Terrain.MakeBlockValue(CoalChunkBlock.Index);
        Pickable tombstone = subsystem.AddPickable(
            value,
            2,
            origin + new Vector3(0f, 0f, 3f),
            Vector3.Zero,
            null,
            null)
            ?? throw new AutomationCommandException(
                "fixture_failed",
                "The authoritative subsystem rejected the tombstone pickable.");
        tombstone.MaxTimeExist = 3600f;
        tombstone.Velocity = Vector3.Zero;
        pickableFixtureAliases.Add(command.TombstoneAlias, tombstone.Id);
        long staleRevision = PickableLifecycleTracker.GetAuthoritativeRevision(
            project,
            tombstone.Id);
        PickableCreatePacket staleCreate = new(tombstone, staleRevision);

        IReadOnlyDictionary<string, string> data = new Dictionary<string, string> {
            ["pickableId"] = tombstone.Id.ToString(CultureInfo.InvariantCulture),
            ["staleRevision"] = staleRevision.ToString(CultureInfo.InvariantCulture),
            ["staleCreateQueued"] = command.SendStaleCreate.ToString(CultureInfo.InvariantCulture)
        };
        RecordPickableAction(
            context,
            "lifecycleBurst",
            ActionEventPhase.Queued,
            command.TombstoneAlias,
            "queued",
            data,
            ActionProvenance.FaultInjection,
            ProductionPathEntries.PickableLifecycle);
        tombstone.ToRemove = true;
        if (command.SendStaleCreate) {
            QueueFaultInjectionAfterSettle(staleCreate);
        }
        RecordPickableAction(
            context,
            "lifecycleBurst",
            ActionEventPhase.Applied,
            command.TombstoneAlias,
            command.SendStaleCreate ? "removal_and_stale_create_queued" : "removal_queued",
            data,
            ActionProvenance.FaultInjection,
            ProductionPathEntries.PickableLifecycle);
        ActionReceipt receipt = new(
            context.RequestId,
            true,
            Time.FrameIndex,
            -1,
            command.SendStaleCreate ? "stale_create_queued" : "removal_queued",
            Provenance: ActionProvenance.FaultInjection,
            ProductionEntry: ProductionPathEntries.PickableLifecycle);
        return new PickableLifecycleBurstResult(
            receipt,
            command.TombstoneAlias,
            tombstone.Id,
            value,
            staleRevision,
            checked(staleRevision + 1));
    }

    private Pickable ResolvePickableAlias(string alias) {
        if (!pickableFixtureAliases.TryGetValue(alias, out int pickableId)) {
            throw new AutomationCommandException(
                "unknown_alias",
                $"Pickable fixture alias '{alias}' is not registered.");
        }
        SubsystemPickables subsystem = GameManager.Project?.FindSubsystem<SubsystemPickables>()
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        Pickable? result = subsystem.Pickables.FirstOrDefault(item => item.Id == pickableId);
        return result
            ?? throw new AutomationCommandException(
                "pickable_missing",
                $"Pickable fixture alias '{alias}' no longer resolves to pickable {pickableId}.");
    }

    private static PickableSnapshotV2[] BuildPickableSnapshotsV2(
        Project project,
        int[]? selectedIds) {
        SubsystemPickables subsystem = project.FindSubsystem<SubsystemPickables>(true);
        int[] ids = (selectedIds ?? subsystem.Pickables
                .Select(pickable => pickable.Id)
                .Where(id => id > 0)
                .ToArray())
            .Distinct()
            .OrderBy(id => id)
            .Take(AutomationProtocol.MaximumSnapshotEntities)
            .ToArray();
        return ids.Select(id => {
                Pickable? pickable = subsystem.Pickables.FirstOrDefault(item => item.Id == id);
                long revision = NetworkManager.IsClientRunning
                    ? PickableLifecycleTracker.GetAppliedRevision(project, id)
                    : PickableLifecycleTracker.GetAuthoritativeRevision(project, id);
                bool tombstone = pickable == null
                    && revision >= 0
                    && (NetworkManager.IsClientRunning
                        ? !PickableLifecycleTracker.IsPresent(project, id)
                        : revision > 0);
                return new PickableSnapshotV2(
                    id,
                    pickable != null,
                    pickable?.Value ?? 0,
                    pickable?.Count ?? 0,
                    ToVector(pickable?.Position ?? Vector3.Zero),
                    ToVector(pickable?.Velocity ?? Vector3.Zero),
                    pickable?.FlyToPosition is Vector3 target ? ToVector(target) : null,
                    revision,
                    tombstone);
            })
            .ToArray();
    }

    private static PickableLifecycleSnapshotV2 BuildPickableLifecycleSnapshotV2(
        Project project) {
        PickableLifecycleMetrics metrics = PickableLifecycleTracker.GetMetrics(project);
        return new PickableLifecycleSnapshotV2(
            metrics.AuthoritativeTrackedPickables,
            metrics.ClientTrackedPickables,
            metrics.AppliedCreates,
            metrics.AppliedUpdates,
            metrics.AppliedRemovals,
            metrics.IgnoredStalePackets,
            metrics.MissingUpdateRequests,
            metrics.Tombstones);
    }

    private static ProjectileSnapshotV2[] BuildProjectileSnapshotsV2(
        Project project,
        long[]? selectedEventIds) {
        SubsystemProjectiles subsystem = project.FindSubsystem<SubsystemProjectiles>(true);
        HashSet<long>? selected = selectedEventIds?.ToHashSet();
        return subsystem.Projectiles
            .Where(projectile => projectile.NetworkEventId > 0
                && (selected == null || selected.Contains(projectile.NetworkEventId)))
            .OrderBy(projectile => projectile.NetworkEventId)
            .Take(AutomationProtocol.MaximumSnapshotEntities)
            .Select(projectile => new ProjectileSnapshotV2(
                projectile.NetworkEventId,
                projectile.Value,
                ToVector(projectile.Position),
                ToVector(projectile.Velocity),
                ToVector(projectile.AngularVelocity),
                projectile.OwnerEntity?.Id,
                projectile.IsIncendiary,
                projectile.ToRemove))
            .ToArray();
    }

    private static ProjectileEventSnapshotV2 BuildProjectileEventSnapshotV2(Project project) {
        ProjectileEventMetrics metrics = ProjectileEventTracker.GetMetrics(project);
        return new ProjectileEventSnapshotV2(
            metrics.GeneratedEvents,
            metrics.AcceptedEvents,
            metrics.IgnoredDuplicateEvents,
            metrics.IgnoredExpiredEvents,
            metrics.HighestGeneratedEventId,
            metrics.HighestAcceptedEventId,
            metrics.RememberedEventIds);
    }

    private static void BuildPickableProjectileArena(
        SubsystemTerrain terrain,
        Vector3 actorPosition,
        Vector3 persistentPosition,
        int floorY) {
        int actorX = Terrain.ToCell(actorPosition.X);
        int actorZ = Terrain.ToCell(actorPosition.Z);
        int pickableX = Terrain.ToCell(persistentPosition.X);
        int pickableZ = Terrain.ToCell(persistentPosition.Z);
        int floorValue = Terrain.MakeBlockValue(CobblestoneBlock.Index);
        for (int x = Math.Min(actorX, pickableX) - 2;
             x <= Math.Max(actorX, pickableX) + 2;
             x++) {
            for (int z = Math.Min(actorZ, pickableZ) - 3;
                 z <= Math.Max(actorZ, pickableZ) + 3;
                 z++) {
                terrain.ChangeCell(x, floorY, z, floorValue);
                for (int y = floorY + 1; y <= floorY + 6; y++) {
                    terrain.ChangeCell(x, y, z, 0);
                }
            }
        }
    }

    private void EnsurePickableServerRole(string operation) {
        if (nodeRole != "server" || !NetworkManager.IsServerRunning) {
            throw new AutomationCommandException(
                "permission_denied",
                $"{operation} must originate from the server node.");
        }
    }

    private void RecordPickableAction(
        AutomationCommandContext context,
        string eventName,
        ActionEventPhase phase,
        string target,
        string code,
        IReadOnlyDictionary<string, string> data,
        ActionProvenance provenance,
        string productionEntry) =>
        actionEventJournal.Record(
            Time.FrameIndex,
            context.RequestId,
            nodeRole,
            AutomationCommandDomain.Pickable,
            eventName,
            phase,
            target: target,
            code: code,
            data: data,
            provenance: provenance,
            productionEntry: productionEntry);

    private void RecordProjectileEvent(
        PendingProjectileAction pending,
        ActionEventPhase phase,
        string code,
        ComponentPlayer.AimEvent actual,
        ComponentPlayer? player = null) =>
        actionEventJournal.Record(
            Time.FrameIndex,
            pending.ActionId,
            nodeRole,
            AutomationCommandDomain.Projectile,
            "fireProjectile",
            phase,
            pending.PlayerIndex,
            code: code,
            data: ProjectileActionData(pending, actual.ToString(), player),
            provenance: ActionProvenance.ProductionInput,
            productionEntry: ProductionPathEntries.PlayerProjectileFire);

    private static IReadOnlyDictionary<string, string> ProjectileActionData(
        PendingProjectileAction pending,
        string inputEvent,
        ComponentPlayer? player = null) {
        Dictionary<string, string> data = new() {
            ["direction"] = FormatVector(pending.Direction),
            ["inputEvent"] = inputEvent,
            ["inputEndFrame"] = pending.InputEndFrame.ToString(CultureInfo.InvariantCulture),
            ["deadlineFrame"] = pending.DeadlineFrame.ToString(CultureInfo.InvariantCulture),
            ["inputObserved"] = pending.InputObserved.ToString(CultureInfo.InvariantCulture),
            ["aimInjectionCount"] = InputHijackState.AimInjectionCount.ToString(CultureInfo.InvariantCulture),
            ["lastAimInjectionFrame"] = InputHijackState.LastAimInjectionFrame.ToString(CultureInfo.InvariantCulture)
        };
        if (player == null) {
            return data;
        }

        int activeValue = player.ComponentMiner.ActiveBlockValue;
        Block block = BlocksManager.Blocks[Terrain.ExtractContents(activeValue)];
        Camera camera = player.GameWidget.ActiveCamera;
        Vector3 projected = camera.WorldToScreen(
            camera.ViewPosition + pending.Direction,
            Matrix.Identity);
        data["playerInputHasAim"] = player.ComponentInput.PlayerInput.Aim.HasValue
            .ToString(CultureInfo.InvariantCulture);
        data["activeBlock"] = block.GetType().Name;
        data["activeValue"] = activeValue.ToString(CultureInfo.InvariantCulture);
        data["activeBlockAimable"] = block.IsAimable_(activeValue)
            .ToString(CultureInfo.InvariantCulture);
        data["localAimActive"] = player.m_aim.HasValue.ToString(CultureInfo.InvariantCulture);
        data["aimBlocked"] = player.m_isAimBlocked.ToString(CultureInfo.InvariantCulture);
        data["aimEvent"] = player.aimEvent.aimEvent.ToString();
        data["lastAimEvent"] = player.last_aimEvent.aimEvent.ToString();
        data["actionTimeGap"] = (player.m_subsystemTime.GameTime - player.m_lastActionTime)
            .ToString("R", CultureInfo.InvariantCulture);
        data["windowSize"] = $"{Window.Size.X},{Window.Size.Y}";
        data["cameraDirection"] = FormatVector(camera.ViewDirection);
        data["cameraPosition"] = FormatVector(camera.ViewPosition);
        data["viewSize"] = FormatVector(new Vector3(
            player.GameWidget.ViewWidget.ActualSize.X,
            player.GameWidget.ViewWidget.ActualSize.Y,
            0f));
        data["projectedAim"] = FormatVector(projected);
        return data;
    }
}
