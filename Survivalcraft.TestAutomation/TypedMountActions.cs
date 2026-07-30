using System.Globalization;
using Engine;
using Game;
using Game.Network;
using GameEntitySystem;
using Survivalcraft.Testing.Control;

namespace Survivalcraft.TestAutomation;

public partial class TestAutomationModLoader {
    private static readonly HashSet<string> SupportedMountFixtureTemplates =
        new(StringComparer.Ordinal) { "Boat" };

    private readonly Dictionary<string, int> mountFixtureAliases =
        new(StringComparer.Ordinal);

    private void ResetMountFixtures() => mountFixtureAliases.Clear();

    private MountFixture PrepareMountFixture(PrepareMountFixtureCommand command) {
        EnsureServerFixtureRole("Mount fixtures");
        ValidateEntityAlias(command.Alias, nameof(command.Alias));
        if (!SupportedMountFixtureTemplates.Contains(command.TemplateName)) {
            throw new AutomationCommandException(
                "invalid_payload",
                $"Unsupported mount fixture template '{command.TemplateName}'.");
        }
        if (!double.IsFinite(command.Distance)
            || command.Distance is < 0.25 or > 64) {
            throw new AutomationCommandException(
                "invalid_payload",
                "Mount fixture distance must be finite and between 0.25 and 64 blocks.");
        }
        if (mountFixtureAliases.ContainsKey(command.Alias)) {
            throw new AutomationCommandException(
                "duplicate_alias",
                $"Mount fixture alias '{command.Alias}' already exists.");
        }

        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        ComponentPlayer actor = ResolvePlayer(
            project.FindSubsystem<SubsystemPlayers>(true),
            command.PlayerIndex,
            requireMainPlayer: false);
        if (actor.PlayerData.IsMainPlayer) {
            throw new AutomationCommandException(
                "invalid_actor",
                "The mount fixture expects a connected remote player.");
        }
        ComponentRider rider = actor.Entity.FindComponent<ComponentRider>()
            ?? throw new AutomationCommandException(
                "invalid_actor",
                $"Player {command.PlayerIndex} has no rider component.");
        EnsureImmediateDismount(project, rider);

        ComponentBody riderBody = actor.ComponentBody;
        riderBody.Velocity = Vector3.Zero;
        riderBody.IsGravityEnabled = false;
        riderBody.IsSmoothRiseEnabled = false;
        actor.ComponentLocomotion.IsCreativeFlyEnabled = true;

        Entity mountEntity = CreateStableEntity(
            project,
            command.TemplateName,
            riderBody.Position);
        ComponentMount mount = mountEntity.FindComponent<ComponentMount>()
            ?? throw new AutomationCommandException(
                "invalid_fixture",
                $"Template '{command.TemplateName}' has no mount component.");
        ComponentBody mountBody = mount.ComponentBody;
        mountBody.Position = riderBody.Position
            + new Vector3((float)command.Distance, 0f, 0f)
            - Vector3.Transform(mount.MountOffset, mountBody.Rotation);
        mountBody.Velocity = Vector3.Zero;
        mountBody.IsGravityEnabled = false;
        mountBody.TerrainCollidable = false;
        mountBody.BodyCollidable = false;
        project.AddEntity(mountEntity);
        mountFixtureAliases.Add(command.Alias, mountEntity.Id);

        QueueFixtureSynchronization(ComponentMountPacket.CreateState(
            rider,
            rider.AuthoritativeMount));
        return CreateMountFixture(
            command.Alias,
            command.TemplateName,
            command.PlayerIndex,
            rider,
            mountEntity);
    }

    private ActionReceipt QueuePlayerMount(
        AutomationCommandContext context,
        PlayerMountCommand command) {
        EnsureClientActionRole("Player mount actions");
        if (command.MountEntityId <= 0) {
            throw new AutomationCommandException(
                "invalid_payload",
                "Mount entityId must be positive.");
        }
        if (command.ForgeAuthoritativeState && !command.ExpectRejected) {
            throw new AutomationCommandException(
                "invalid_payload",
                "Forged mount state must be an explicit rejection probe.");
        }
        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        ComponentPlayer actor = ResolvePlayer(
            project.FindSubsystem<SubsystemPlayers>(true),
            command.PlayerIndex,
            requireMainPlayer: true);
        ComponentRider rider = actor.Entity.FindComponent<ComponentRider>()
            ?? throw new AutomationCommandException(
                "invalid_actor",
                $"Player {command.PlayerIndex} has no rider component.");
        ComponentMount mount = project.FindEntity(command.MountEntityId)
            ?.FindComponent<ComponentMount>()
            ?? throw new AutomationCommandException(
                "mount_not_found",
                $"Entity {command.MountEntityId} is not an available mount.");
        int riderEntityId = command.TargetRiderEntityId ?? rider.Entity.Id;
        RecordMountAction(
            context.RequestId,
            command.Mount,
            ActionEventPhase.Queued,
            command.PlayerIndex,
            riderEntityId,
            command.MountEntityId,
            command.ForgeAuthoritativeState ? "forged_authoritative_state" : "request");

        ComponentMountPacket packet;
        if (command.ForgeAuthoritativeState) {
            packet = new ComponentMountPacket {
                m_type = ComponentMountPacket.EventType.State,
                m_result = ComponentMountPacket.ResultCode.Accepted,
                m_riderEntityId = riderEntityId,
                m_mountEntityId = command.Mount ? command.MountEntityId : 0,
                m_riderLifecycleRevision = EntityLifecycleTracker.GetAppliedRevision(
                    project,
                    riderEntityId),
                m_mountLifecycleRevision = command.Mount
                    ? EntityLifecycleTracker.GetAppliedRevision(project, command.MountEntityId)
                    : -1,
                m_revision = long.MaxValue / 2,
                m_requestId = 0
            };
        }
        else {
            packet = command.Mount
                ? new ComponentMountPacket(rider, mount, isRequest: true)
                : new ComponentMountPacket(rider, isRequest: true);
            packet.m_riderEntityId = riderEntityId;
        }
        if (command.ExpectRejected) {
            QueueFaultInjection(packet);
        }
        else {
            QueueProductionRequest(packet);
        }
        RecordMountAction(
            context.RequestId,
            command.Mount,
            ActionEventPhase.Applied,
            command.PlayerIndex,
            riderEntityId,
            command.MountEntityId,
            command.ExpectRejected ? "rejection_probe_queued" : "production_packet_queued");
        return new ActionReceipt(
            context.RequestId,
            true,
            Time.FrameIndex,
            command.PlayerIndex,
            command.ExpectRejected ? "rejection_probe_queued" : "queued",
            Provenance: command.ExpectRejected
                ? ActionProvenance.FaultInjection
                : ActionProvenance.ProductionRequest,
            ProductionEntry: ProductionPathEntries.MountState);
    }

    private MountRevisionBurstResult RunMountRevisionBurst(
        AutomationCommandContext context,
        RunMountRevisionBurstCommand command) {
        EnsureServerFixtureRole("Mount revision bursts");
        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        ComponentRider rider = ResolveRider(project, command.RiderEntityId);
        ComponentMount mount = ResolveMount(project, command.MountEntityId);
        ComponentMountPacket stale = ComponentMountPacket.CreateState(
            rider,
            rider.AuthoritativeMount);
        long staleRevision = stale.m_revision;
        RecordMountAction(
            context.RequestId,
            command.FinalMounted,
            ActionEventPhase.Queued,
            -1,
            command.RiderEntityId,
            command.MountEntityId,
            "revision_burst");

        ApplyAndQueueMountState(project, rider, command.FinalMounted ? null : mount);
        long finalRevision = ApplyAndQueueMountState(
            project,
            rider,
            command.FinalMounted ? mount : null);
        if (command.SendStaleState) {
            QueueFaultInjectionAfterSettle(stale);
        }
        RecordMountAction(
            context.RequestId,
            command.FinalMounted,
            ActionEventPhase.Applied,
            -1,
            command.RiderEntityId,
            command.MountEntityId,
            command.SendStaleState ? "stale_state_queued" : "final_state_queued");
        ActionReceipt receipt = new(
            context.RequestId,
            true,
            Time.FrameIndex,
            -1,
            command.SendStaleState ? "stale_state_queued" : "applied",
            Provenance: ActionProvenance.FaultInjection,
            ProductionEntry: ProductionPathEntries.MountState);
        return new MountRevisionBurstResult(
            receipt,
            command.RiderEntityId,
            command.MountEntityId,
            command.FinalMounted,
            staleRevision,
            finalRevision);
    }

    private MountLifecycleBurstResult RunMountLifecycleBurst(
        AutomationCommandContext context,
        RunMountLifecycleBurstCommand command) {
        EnsureServerFixtureRole("Mount lifecycle bursts");
        ValidateEntityAlias(command.Alias, nameof(command.Alias));
        if (!mountFixtureAliases.TryGetValue(command.Alias, out int mountEntityId)) {
            throw new AutomationCommandException(
                "unknown_alias",
                $"Mount fixture alias '{command.Alias}' is not registered.");
        }
        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        ComponentRider rider = ResolveRider(project, command.RiderEntityId);
        ComponentMount mount = ResolveMount(project, mountEntityId);
        Entity removedEntity = mount.Entity;
        string templateName = removedEntity.ValuesDictionary.DatabaseObject.Name;
        int playerIndex = rider.Entity.FindComponent<ComponentPlayer>()?.PlayerData.PlayerIndex ?? -1;
        if (rider.AuthoritativeMount != mount) {
            ApplyAndQueueMountState(project, rider, mount);
        }
        ComponentMountPacket staleLifecycleState = ComponentMountPacket.CreateState(rider, mount);
        long staleLifecycleRevision = staleLifecycleState.m_mountLifecycleRevision;
        long revisionBeforeRemoval = staleLifecycleState.m_revision;
        Vector3 replacementPosition = mount.ComponentBody.Position;

        RecordMountAction(
            context.RequestId,
            false,
            ActionEventPhase.Queued,
            -1,
            command.RiderEntityId,
            mountEntityId,
            "lifecycle_burst");
        project.RemoveEntity(removedEntity, true);
        long authoritativeMountRevision = NetworkStateRevisionTracker.GetAuthoritativeRevision(
            project,
            NetworkStateRevisionDomain.Mount,
            rider.Entity.Id);
        if (authoritativeMountRevision <= revisionBeforeRemoval) {
            rider.ApplyAuthoritativeMountState(null, animate: false);
            authoritativeMountRevision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
                project,
                NetworkStateRevisionDomain.Mount,
                rider.Entity.Id);
            QueueAuthoritativeState(ComponentMountPacket.CreateState(
                rider,
                null,
                authoritativeMountRevision));
        }

        Entity replacement = CreateStableEntity(project, templateName, replacementPosition);
        if (command.ReuseEntityId) {
            replacement.Id = mountEntityId;
        }
        project.AddEntity(replacement);
        mountFixtureAliases[command.Alias] = replacement.Id;
        long finalLifecycleRevision = EntityLifecycleTracker.GetAuthoritativeRevision(
            project,
            replacement.Id);

        long injectedStaleRevision = staleLifecycleState.m_revision;
        if (command.SendStaleState) {
            int staleDeliveryFrame = checked(Time.FrameIndex + 30);
            Time.QueueFrameIndexDelayedExecution(
                staleDeliveryFrame,
                () => QueueFaultInjection(staleLifecycleState));
        }
        QueueAuthoritativeState(ComponentMountPacket.CreateState(
            rider,
            null,
            authoritativeMountRevision));

        MountFixture fixture = CreateMountFixture(
            command.Alias,
            templateName,
            playerIndex,
            rider,
            replacement);
        RecordMountAction(
            context.RequestId,
            false,
            ActionEventPhase.Applied,
            -1,
            command.RiderEntityId,
            replacement.Id,
            command.SendStaleState
                ? "stale_lifecycle_state_scheduled"
                : "replacement_created");
        ActionReceipt receipt = new(
            context.RequestId,
            true,
            Time.FrameIndex,
            -1,
            command.SendStaleState ? "stale_lifecycle_state_queued" : "applied",
            Provenance: ActionProvenance.FaultInjection,
            ProductionEntry: ProductionPathEntries.MountState);
        return new MountLifecycleBurstResult(
            receipt,
            fixture,
            staleLifecycleRevision,
            finalLifecycleRevision,
            authoritativeMountRevision,
            injectedStaleRevision);
    }

    private static ComponentRider ResolveRider(Project project, int entityId) {
        if (entityId <= 0) {
            throw new AutomationCommandException(
                "invalid_payload",
                "Rider entityId must be positive.");
        }
        return project.FindEntity(entityId)?.FindComponent<ComponentRider>()
            ?? throw new AutomationCommandException(
                "rider_not_found",
                $"Entity {entityId} is not a rider.");
    }

    private static ComponentMount ResolveMount(Project project, int entityId) {
        if (entityId <= 0) {
            throw new AutomationCommandException(
                "invalid_payload",
                "Mount entityId must be positive.");
        }
        return project.FindEntity(entityId)?.FindComponent<ComponentMount>()
            ?? throw new AutomationCommandException(
                "mount_not_found",
                $"Entity {entityId} is not a mount.");
    }

    private static long ApplyAndQueueMountState(
        Project project,
        ComponentRider rider,
        ComponentMount? mount) {
        if (!rider.ApplyAuthoritativeMountState(mount, animate: false)) {
            throw new AutomationCommandException(
                "invalid_state",
                "The authoritative mount relationship could not be applied.");
        }
        long revision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
            project,
            NetworkStateRevisionDomain.Mount,
            rider.Entity.Id);
        QueueAuthoritativeState(ComponentMountPacket.CreateState(rider, mount, revision));
        return revision;
    }

    private static void EnsureImmediateDismount(Project project, ComponentRider rider) {
        if (rider.AuthoritativeMount == null && rider.Mount == null) {
            return;
        }
        rider.ApplyAuthoritativeMountState(null, animate: false);
        long revision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
            project,
            NetworkStateRevisionDomain.Mount,
            rider.Entity.Id);
        QueueFixtureSynchronization(ComponentMountPacket.CreateState(rider, null, revision));
    }

    private static MountFixture CreateMountFixture(
        string alias,
        string templateName,
        int playerIndex,
        ComponentRider rider,
        Entity mountEntity) {
        Project project = rider.Entity.Project;
        ComponentBody mountBody = mountEntity.FindComponent<ComponentBody>(true);
        return new MountFixture(
            alias,
            templateName,
            playerIndex,
            rider.Entity.Id,
            EntityLifecycleTracker.GetAuthoritativeRevision(project, rider.Entity.Id),
            mountEntity.Id,
            EntityLifecycleTracker.GetAuthoritativeRevision(project, mountEntity.Id),
            NetworkStateRevisionTracker.GetAuthoritativeRevision(
                project,
                NetworkStateRevisionDomain.Mount,
                rider.Entity.Id),
            ToVector(mountBody.Position));
    }

    private void RecordMountAction(
        string actionId,
        bool mount,
        ActionEventPhase phase,
        int playerIndex,
        int riderEntityId,
        int mountEntityId,
        string code) {
        ActionProvenance provenance = MountEvidence(code);
        actionEventJournal.Record(
            Time.FrameIndex,
            actionId,
            nodeRole,
            AutomationCommandDomain.Mechanics,
            mount ? "mount" : "dismount",
            phase,
            playerIndex >= 0 ? playerIndex : null,
            riderEntityId.ToString(CultureInfo.InvariantCulture),
            code,
            new Dictionary<string, string> {
                ["riderEntityId"] = riderEntityId.ToString(CultureInfo.InvariantCulture),
                ["mountEntityId"] = mountEntityId.ToString(CultureInfo.InvariantCulture)
            },
            provenance: provenance,
            productionEntry: ProductionPathEntries.MountState);
    }

    private static ActionProvenance MountEvidence(string code) {
        string[] faultMarkers = [
            "forged_",
            "rejection_probe",
            "revision_burst",
            "stale_",
            "final_state_",
            "lifecycle_burst",
            "replacement_"
        ];
        return faultMarkers.Any(marker => code.Contains(marker, StringComparison.Ordinal))
            ? ActionProvenance.FaultInjection
            : ActionProvenance.ProductionRequest;
    }
}
