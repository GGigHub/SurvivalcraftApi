using System.Globalization;
using Engine;
using Game;
using Game.Network;
using GameEntitySystem;
using Survivalcraft.Testing.Control;
using Survivalcraft.Testing.Snapshots;

namespace Survivalcraft.TestAutomation;

public partial class TestAutomationModLoader {
    private const int TerrainStreamingFixtureLocationIndex = int.MinValue + 1701;
    private const float TerrainStreamingMoveThreshold = 8f;
    private const float TerrainStreamingFixtureFlySpeed = 320f;

    private PendingTerrainStreamingMoveTrigger? pendingTerrainStreamingMoveTrigger;

    private sealed record PendingTerrainStreamingMoveTrigger(
        int PlayerIndex,
        Vector3 BaselinePosition,
        float VisibilityDistance,
        float ContentDistance);

    private TerrainStreamingFixture PrepareTerrainStreamingFixture(
        PrepareTerrainStreamingFixtureCommand command) {
        if (nodeRole != "server" || !NetworkManager.IsServerRunning) {
            throw new AutomationCommandException(
                "permission_denied",
                "Terrain streaming fixtures must be prepared by the server node.");
        }
        if (command.ActorPlayerIndex == command.ObserverPlayerIndex) {
            throw new AutomationCommandException(
                "invalid_payload",
                "Actor and observer player indexes must be different.");
        }
        EnsureNoTypedPlayerActionInProgress();
        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        SubsystemPlayers players = project.FindSubsystem<SubsystemPlayers>(true);
        SubsystemTerrain terrain = project.FindSubsystem<SubsystemTerrain>(true);
        ComponentPlayer actor = ResolvePlayer(players, command.ActorPlayerIndex, requireMainPlayer: false);
        ComponentPlayer observer = ResolvePlayer(players, command.ObserverPlayerIndex, requireMainPlayer: false);
        if (actor.PlayerData.IsMainPlayer || observer.PlayerData.IsMainPlayer) {
            throw new AutomationCommandException(
                "invalid_actor",
                "Terrain streaming fixtures expect connected remote players.");
        }

        Vector3 spawn = players.GlobalSpawnPosition;
        int spawnChunkX = Terrain.ToCell(spawn.X) >> 4;
        int spawnChunkZ = Terrain.ToCell(spawn.Z) >> 4;
        int targetChunkX = checked(spawnChunkX + 24);
        int targetChunkZ = checked(spawnChunkZ + 24);
        float targetCenterX = (targetChunkX + 0.5f) * TerrainChunk.Size;
        float targetCenterZ = (targetChunkZ + 0.5f) * TerrainChunk.Size;
        int floorY = Math.Clamp((int)MathF.Floor(spawn.Y) - 1, 8, 238);
        const float visibilityDistance = 48f;
        const float contentDistance = 64f;
        Vector3 actorPosition = new(
            targetCenterX,
            floorY + 1.01f,
            targetCenterZ + contentDistance + 0.25f);
        Vector3 observerPosition = new(
            targetCenterX,
            floorY + 1.01f,
            targetCenterZ + 32f);
        TerrainCellCoordinate probe = new(
            targetChunkX * TerrainChunk.Size + 8,
            floorY + 1,
            targetChunkZ * TerrainChunk.Size + 8);

        Vector2 fixtureCenter = new(
            targetCenterX,
            (targetCenterZ + actorPosition.Z) * 0.5f);
        terrain.TerrainUpdater.SetUpdateLocation(
            TerrainStreamingFixtureLocationIndex,
            fixtureCenter,
            visibilityDistance,
            96f);
        terrain.TerrainUpdater.UpdateFullySynchronously();

        int standingValue = Terrain.MakeBlockValue(GraniteBlock.Index);
        BuildStreamingPlatform(terrain, actorPosition, floorY, standingValue);
        BuildStreamingPlatform(terrain, observerPosition, floorY, standingValue);
        int requestedInitialValue = Terrain.MakeBlockValue(GlassBlock.Index);
        int[] revisionValues = [
            Terrain.MakeBlockValue(DirtBlock.Index),
            Terrain.MakeBlockValue(PlanksBlock.Index),
            Terrain.MakeBlockValue(SandstoneBlock.Index)
        ];
        terrain.ChangeCell(probe.X, probe.Y, probe.Z, requestedInitialValue);
        terrain.TerrainUpdater.UpdateFullySynchronously();
        int initialValue = Terrain.ReplaceLight(
            terrain.Terrain.GetCellValue(probe.X, probe.Y, probe.Z),
            0);

        PrepareStreamingPlayer(actor, actorPosition, standingValue, freezeBody: true);
        PrepareStreamingPlayer(observer, observerPosition, standingValue, freezeBody: true);

        return new TerrainStreamingFixture(
            command.ActorPlayerIndex,
            command.ObserverPlayerIndex,
            ToVector(actorPosition),
            ToVector(observerPosition),
            probe,
            initialValue,
            revisionValues.Select(value => Terrain.ReplaceLight(value, 0)).ToArray(),
            standingValue,
            visibilityDistance,
            contentDistance,
            new Vector3State(0, 0, 0.75),
            45);
    }

    private ControlAck ApplyTerrainStreamingView(ApplyTerrainStreamingViewCommand command) {
        if (!nodeRole.StartsWith("client", StringComparison.Ordinal)
            || !NetworkManager.IsClientRunning) {
            throw new AutomationCommandException(
                "permission_denied",
                "Terrain streaming views must be applied by a connected client node.");
        }
        if (command.Position == null
            || !IsFinite(ToVector(command.Position))
            || !double.IsFinite(command.VisibilityDistance)
            || !double.IsFinite(command.ContentDistance)
            || command.VisibilityDistance is < 16 or > 256
            || command.ContentDistance < command.VisibilityDistance
            || command.ContentDistance > 256
            || command.StandingValue <= 0) {
            throw new AutomationCommandException(
                "invalid_payload",
                "Terrain streaming view parameters are invalid.");
        }
        EnsureNoTypedPlayerActionInProgress();
        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        SubsystemPlayers players = project.FindSubsystem<SubsystemPlayers>(true);
        SubsystemTerrain terrain = project.FindSubsystem<SubsystemTerrain>(true);
        SubsystemSky sky = project.FindSubsystem<SubsystemSky>(true);
        ComponentPlayer player = ResolvePlayer(players, command.PlayerIndex, requireMainPlayer: true);
        Vector3 position = ToVector(command.Position);

        sky.VisibilityRange = (float)command.VisibilityDistance;
        PrepareStreamingPlayer(player, position, command.StandingValue, command.FreezeBody);
        TerrainUpdater updater = terrain.TerrainUpdater;
        updater.SetUpdateLocation(
            command.PlayerIndex,
            position.XZ,
            (float)command.VisibilityDistance,
            (float)command.ContentDistance);
        if (command.ArmMoveThreshold) {
            if (!updater.m_pendingLocations.ContainsKey(command.PlayerIndex)
                && !updater.UpdateLocations.ContainsKey(command.PlayerIndex)) {
                throw new AutomationCommandException(
                    "invalid_state",
                    "The client terrain update location was not created.");
            }
            pendingTerrainStreamingMoveTrigger = new PendingTerrainStreamingMoveTrigger(
                command.PlayerIndex,
                position,
                (float)command.VisibilityDistance,
                (float)command.ContentDistance);
        }
        updater.RequestSynchronousUpdate();
        QueueFixtureSynchronization(new PlayerBodyUpdatePacket(player, forceFull: true));
        return new ControlAck(
            true,
            command.ArmMoveThreshold
                ? "Terrain view applied and movement hysteresis armed."
                : "Terrain view applied.");
    }

    private void HandleTerrainStreamingMoveTrigger(
        ComponentPlayer player,
        bool isMainPlayer) {
        PendingTerrainStreamingMoveTrigger? trigger = pendingTerrainStreamingMoveTrigger;
        if (trigger == null
            || !isMainPlayer
            || player.PlayerData.PlayerIndex != trigger.PlayerIndex) {
            return;
        }
        Vector3 current = player.ComponentBody.Position;
        float x = current.X - trigger.BaselinePosition.X;
        float z = current.Z - trigger.BaselinePosition.Z;
        if (x * x + z * z <= TerrainStreamingMoveThreshold * TerrainStreamingMoveThreshold) {
            return;
        }
        Project? project = GameManager.Project;
        TerrainUpdater? updater = project?.FindSubsystem<SubsystemTerrain>()?.TerrainUpdater;
        if (updater == null
            || !updater.UpdateLocations.TryGetValue(
                trigger.PlayerIndex,
                out TerrainUpdater.UpdateLocation location)) {
            return;
        }

        // Headless automation has no drawing pass, so mirror the production camera handoff
        // only after real ComponentInput movement has crossed TerrainUpdater's native
        // eight-block hysteresis. SetUpdateLocation still owns the TerrainViewDistancePacket
        // event and all downstream streaming behavior on both client and server.
        updater.SetUpdateLocation(
            trigger.PlayerIndex,
            current.XZ,
            trigger.VisibilityDistance,
            trigger.ContentDistance);
        updater.RequestSynchronousUpdate();
        pendingTerrainStreamingMoveTrigger = null;
    }

    private TerrainRevisionBurstResult RunTerrainRevisionBurst(
        AutomationCommandContext context,
        RunTerrainRevisionBurstCommand command) {
        if (nodeRole != "server" || !NetworkManager.IsServerRunning) {
            throw new AutomationCommandException(
                "permission_denied",
                "Terrain revision bursts must originate from the server node.");
        }
        if (command.Cell == null
            || command.Cell.Y is < 0 or > 255
            || command.Values is not { Length: >= 2 and <= 64 }
            || command.Values.Any(value => value < 0)) {
            throw new AutomationCommandException(
                "invalid_payload",
                "A revision burst requires a valid cell and between 2 and 64 block values.");
        }
        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        SubsystemTerrain terrain = project.FindSubsystem<SubsystemTerrain>(true);
        int chunkX = command.Cell.X >> 4;
        int chunkZ = command.Cell.Z >> 4;
        TerrainChunk chunk = terrain.Terrain.GetChunkAtCoords(chunkX, chunkZ)
            ?? throw new AutomationCommandException(
                "invalid_state",
                "The revision probe chunk is not allocated on the server.");
        if (chunk.ThreadState <= TerrainChunkState.InvalidContents4) {
            throw new AutomationCommandException(
                "invalid_state",
                "The revision probe chunk is not content-ready on the server.");
        }

        TerrainPacketBase.TerrainChunkSyncData staleSnapshot =
            TerrainPacketBase.CreateChunkSyncData(terrain, chunk);
        long tick = Time.FrameIndex;
        string target = FormatStreamingCell(command.Cell);
        actionEventJournal.Record(
            tick,
            context.RequestId,
            nodeRole,
            AutomationCommandDomain.Terrain,
            "terrainRevisionBurst",
            ActionEventPhase.Queued,
            target: target,
            code: "queued",
            data: RevisionActionData(staleSnapshot.Revision, staleSnapshot.Revision, command.Values.Length),
            provenance: ActionProvenance.FaultInjection,
            productionEntry: ProductionPathEntries.TerrainRevision);

        foreach (int value in command.Values) {
            terrain.ChangeCell(
                command.Cell.X,
                command.Cell.Y,
                command.Cell.Z,
                Terrain.ReplaceLight(value, 0));
        }
        long finalRevision = TerrainRevisionTracker.GetAuthoritativeRevision(
            terrain,
            chunkX,
            chunkZ);
        int finalValue = Terrain.ReplaceLight(
            terrain.Terrain.GetCellValue(command.Cell.X, command.Cell.Y, command.Cell.Z),
            0);
        actionEventJournal.Record(
            Time.FrameIndex,
            context.RequestId,
            nodeRole,
            AutomationCommandDomain.Terrain,
            "terrainRevisionBurst",
            ActionEventPhase.Observed,
            target: target,
            code: "authoritative_revision_observed",
            data: RevisionActionData(staleSnapshot.Revision, finalRevision, command.Values.Length),
            provenance: ActionProvenance.FaultInjection,
            productionEntry: ProductionPathEntries.TerrainRevision);
        if (command.SendStaleSnapshot) {
            QueueFaultInjectionAfterSettle(new TerrainSyncChunkListPacket([staleSnapshot]));
        }
        terrain.TerrainUpdater.RemoveUpdateLocation(TerrainStreamingFixtureLocationIndex);
        terrain.TerrainUpdater.RequestSynchronousUpdate();
        actionEventJournal.Record(
            Time.FrameIndex,
            context.RequestId,
            nodeRole,
            AutomationCommandDomain.Terrain,
            "terrainRevisionBurst",
            ActionEventPhase.Applied,
            target: target,
            code: command.SendStaleSnapshot ? "stale_snapshot_queued" : "applied",
            data: RevisionActionData(staleSnapshot.Revision, finalRevision, command.Values.Length),
            provenance: ActionProvenance.FaultInjection,
            productionEntry: ProductionPathEntries.TerrainRevision);

        ActionReceipt receipt = new(
            context.RequestId,
            true,
            Time.FrameIndex,
            -1,
            command.SendStaleSnapshot ? "stale_snapshot_queued" : "applied",
            Provenance: ActionProvenance.FaultInjection,
            ProductionEntry: ProductionPathEntries.TerrainRevision);
        return new TerrainRevisionBurstResult(
            receipt,
            staleSnapshot.Revision,
            finalRevision,
            finalValue,
            command.Values.Length);
    }

    private static void BuildStreamingPlatform(
        SubsystemTerrain terrain,
        Vector3 position,
        int floorY,
        int standingValue) {
        int centerX = Terrain.ToCell(position.X);
        int centerZ = Terrain.ToCell(position.Z);
        const int radius = 6;
        for (int x = centerX - radius; x <= centerX + radius; x++) {
            for (int z = centerZ - radius; z <= centerZ + radius; z++) {
                terrain.ChangeCell(x, floorY, z, standingValue);
                for (int y = floorY + 1; y <= floorY + 4; y++) {
                    terrain.ChangeCell(x, y, z, 0);
                }
            }
        }
    }

    private static void PrepareStreamingPlayer(
        ComponentPlayer player,
        Vector3 position,
        int standingValue,
        bool freezeBody) {
        ComponentBody body = player.ComponentBody;
        body.Position = position;
        body.Rotation = Quaternion.Identity;
        body.Velocity = Vector3.Zero;
        body.IsGravityEnabled = !freezeBody;
        body.IsGroundDragEnabled = true;
        body.IsSmoothRiseEnabled = !freezeBody;
        body.StandingOnValue = freezeBody ? null : standingValue;
        ComponentLocomotion locomotion = player.ComponentLocomotion;
        locomotion.IsCreativeFlyEnabled = !freezeBody;
        if (!freezeBody) {
            locomotion.CreativeFlySpeed = MathF.Max(
                locomotion.CreativeFlySpeed,
                TerrainStreamingFixtureFlySpeed);
        }
        locomotion.LookAngles = Vector2.Zero;
        locomotion.LookOrder = Vector2.Zero;
        locomotion.TurnOrder = Vector2.Zero;
        InputHijackState.ResetGameplayInputs();
    }

    private static IReadOnlyDictionary<string, string> RevisionActionData(
        long baseline,
        long final,
        int count) =>
        new Dictionary<string, string> {
            ["baselineRevision"] = baseline.ToString(CultureInfo.InvariantCulture),
            ["finalRevision"] = final.ToString(CultureInfo.InvariantCulture),
            ["changeCount"] = count.ToString(CultureInfo.InvariantCulture)
        };

    private static string FormatStreamingCell(TerrainCellCoordinate cell) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{cell.X},{cell.Y},{cell.Z}");
}
