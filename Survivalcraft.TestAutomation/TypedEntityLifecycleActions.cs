using System.Globalization;
using Engine;
using Game;
using Game.Network;
using GameEntitySystem;
using Survivalcraft.Testing.Control;
using Survivalcraft.Testing.Snapshots;

namespace Survivalcraft.TestAutomation;

public partial class TestAutomationModLoader {
    private static readonly HashSet<string> SupportedEntityFixtureTemplates =
        new(StringComparer.Ordinal) { "Cow", "Duck" };

    private readonly Dictionary<string, int> entityFixtureAliases =
        new(StringComparer.Ordinal);
    private Vector3? entityLifecycleFixtureOrigin;

    private EntityLifecycleFixture PrepareEntityLifecycleFixture(
        PrepareEntityLifecycleFixtureCommand command) {
        EnsureEntityServerRole("Entity lifecycle fixtures");
        ValidateEntityAlias(command.Alias, nameof(command.Alias));
        if (!SupportedEntityFixtureTemplates.Contains(command.TemplateName)) {
            throw new AutomationCommandException(
                "invalid_payload",
                $"Unsupported entity fixture template '{command.TemplateName}'.");
        }
        if (entityFixtureAliases.ContainsKey(command.Alias)) {
            throw new AutomationCommandException(
                "duplicate_alias",
                $"Entity fixture alias '{command.Alias}' already exists.");
        }

        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        SubsystemPlayers players = project.FindSubsystem<SubsystemPlayers>(true);
        ComponentPlayer actor = ResolvePlayer(
            players,
            command.ActorPlayerIndex,
            requireMainPlayer: false);
        if (actor.PlayerData.IsMainPlayer) {
            throw new AutomationCommandException(
                "invalid_actor",
                "The entity lifecycle fixture expects a connected remote player.");
        }

        Vector3 position = actor.ComponentBody.Position + new Vector3(6f, 0.25f, 0f);
        Entity entity = CreateStableEntity(project, command.TemplateName, position);
        project.AddEntity(entity);
        entityFixtureAliases.Add(command.Alias, entity.Id);
        entityLifecycleFixtureOrigin = position;
        return CreateEntityFixture(command.Alias, entity);
    }

    private void ResetEntityLifecycleFixtures() {
        entityFixtureAliases.Clear();
        entityLifecycleFixtureOrigin = null;
    }

    private ActionReceipt ApplyEntityImpulse(
        AutomationCommandContext context,
        ApplyEntityImpulseCommand command) {
        EnsureEntityServerRole("Entity impulse actions");
        ValidateEntityAlias(command.Alias, nameof(command.Alias));
        if (command.ActiveFrames is < 2 or > 120) {
            throw new AutomationCommandException(
                "invalid_payload",
                "Entity impulse activeFrames must be between 2 and 120.");
        }
        Vector3 impulse = ToVector(command.Impulse);
        if (!IsFinite(impulse)
            || impulse.LengthSquared() is <= 0.0001f or > 64f) {
            throw new AutomationCommandException(
                "invalid_payload",
                "Entity impulse must be finite, non-zero, and have length at most 8.");
        }

        Entity entity = ResolveEntityAlias(command.Alias);
        ComponentBody body = entity.FindComponent<ComponentBody>(true);
        body.IsGravityEnabled = false;
        body.TerrainCollidable = false;
        body.BodyCollidable = false;
        body.Velocity = Vector3.Zero;
        body.m_totalImpulse = Vector3.Zero;
        int queuedTick = Time.FrameIndex;
        IReadOnlyDictionary<string, string> data = new Dictionary<string, string> {
            ["entityId"] = entity.Id.ToString(CultureInfo.InvariantCulture),
            ["impulse"] = FormatVector(impulse),
            ["activeFrames"] = command.ActiveFrames.ToString(CultureInfo.InvariantCulture)
        };
        actionEventJournal.Record(
            queuedTick,
            context.RequestId,
            nodeRole,
            AutomationCommandDomain.Entity,
            "applyImpulse",
            ActionEventPhase.Queued,
            target: command.Alias,
            code: "queued",
            data: data);

        body.ApplyImpulse(impulse);
        actionEventJournal.Record(
            Time.FrameIndex,
            context.RequestId,
            nodeRole,
            AutomationCommandDomain.Entity,
            "applyImpulse",
            ActionEventPhase.Observed,
            target: command.Alias,
            code: "production_impulse_applied",
            data: data);

        int entityId = entity.Id;
        int completionFrame = checked(Time.FrameIndex + command.ActiveFrames);
        Time.QueueFrameIndexDelayedExecution(completionFrame, () => {
            Project? currentProject = GameManager.Project;
            Entity? current = currentProject?.FindEntity(entityId);
            ComponentBody? currentBody = current?.FindComponent<ComponentBody>();
            if (currentBody == null) {
                actionEventJournal.Record(
                    Time.FrameIndex,
                    context.RequestId,
                    nodeRole,
                    AutomationCommandDomain.Entity,
                    "applyImpulse",
                    ActionEventPhase.Rejected,
                    target: command.Alias,
                    code: "entity_missing",
                    data: data);
                return;
            }
            currentBody.Velocity = Vector3.Zero;
            currentBody.m_totalImpulse = Vector3.Zero;
            actionEventJournal.Record(
                Time.FrameIndex,
                context.RequestId,
                nodeRole,
                AutomationCommandDomain.Entity,
                "applyImpulse",
                ActionEventPhase.Applied,
                target: command.Alias,
                code: "impulse_window_completed",
                data: data);
        });

        return new ActionReceipt(
            context.RequestId,
            true,
            queuedTick,
            -1,
            "queued");
    }

    private EntityLifecycleBurstResult RunEntityLifecycleBurst(
        AutomationCommandContext context,
        RunEntityLifecycleBurstCommand command) {
        EnsureEntityServerRole("Entity lifecycle bursts");
        ValidateEntityAlias(command.ReplacementAlias, nameof(command.ReplacementAlias));
        ValidateEntityAlias(command.TombstoneAlias, nameof(command.TombstoneAlias));
        if (command.ReplacementAlias == command.TombstoneAlias) {
            throw new AutomationCommandException(
                "invalid_payload",
                "Replacement and tombstone aliases must be different.");
        }
        if (entityFixtureAliases.ContainsKey(command.ReplacementAlias)
            || entityFixtureAliases.ContainsKey(command.TombstoneAlias)) {
            throw new AutomationCommandException(
                "duplicate_alias",
                "Lifecycle burst aliases must not already exist.");
        }
        Vector3 origin = entityLifecycleFixtureOrigin
            ?? throw new AutomationCommandException(
                "invalid_state",
                "Prepare the entity lifecycle fixture before running a lifecycle burst.");
        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");

        actionEventJournal.Record(
            Time.FrameIndex,
            context.RequestId,
            nodeRole,
            AutomationCommandDomain.Entity,
            "lifecycleBurst",
            ActionEventPhase.Queued,
            target: $"{command.ReplacementAlias},{command.TombstoneAlias}",
            code: "queued");

        Entity replaced = CreateStableEntity(project, "Cow", origin + new Vector3(10f, 0f, 0f));
        project.AddEntity(replaced);
        EntityLoadPacket staleReplacementLoad = new(replaced);
        long staleReplacementRevision = EntityLifecycleTracker.GetAuthoritativeRevision(
            project,
            replaced.Id);
        int replacementId = replaced.Id;
        project.RemoveEntity(replaced, true);

        Entity replacement = CreateStableEntity(
            project,
            "Cow",
            origin + new Vector3(12f, 0f, 0f));
        replacement.Id = replacementId;
        project.AddEntity(replacement);
        long finalReplacementRevision = EntityLifecycleTracker.GetAuthoritativeRevision(
            project,
            replacement.Id);
        entityFixtureAliases.Add(command.ReplacementAlias, replacement.Id);

        Entity removed = CreateStableEntity(project, "Duck", origin + new Vector3(14f, 0f, 0f));
        project.AddEntity(removed);
        EntityLoadPacket staleRemovedLoad = new(removed);
        long staleTombstoneRevision = EntityLifecycleTracker.GetAuthoritativeRevision(
            project,
            removed.Id);
        int tombstoneEntityId = removed.Id;
        project.RemoveEntity(removed, true);
        long finalTombstoneRevision = EntityLifecycleTracker.GetAuthoritativeRevision(
            project,
            tombstoneEntityId);
        entityFixtureAliases.Add(command.TombstoneAlias, tombstoneEntityId);

        if (command.SendStaleLoads) {
            NetworkManager.Queue(staleReplacementLoad);
            NetworkManager.Queue(staleRemovedLoad);
        }

        IReadOnlyDictionary<string, string> data = new Dictionary<string, string> {
            ["replacementEntityId"] = replacement.Id.ToString(CultureInfo.InvariantCulture),
            ["replacementRevision"] = finalReplacementRevision.ToString(CultureInfo.InvariantCulture),
            ["tombstoneEntityId"] = tombstoneEntityId.ToString(CultureInfo.InvariantCulture),
            ["tombstoneRevision"] = finalTombstoneRevision.ToString(CultureInfo.InvariantCulture),
            ["staleLoadsQueued"] = command.SendStaleLoads.ToString(CultureInfo.InvariantCulture)
        };
        actionEventJournal.Record(
            Time.FrameIndex,
            context.RequestId,
            nodeRole,
            AutomationCommandDomain.Entity,
            "lifecycleBurst",
            ActionEventPhase.Observed,
            target: $"{command.ReplacementAlias},{command.TombstoneAlias}",
            code: "authoritative_lifecycle_observed",
            data: data);
        actionEventJournal.Record(
            Time.FrameIndex,
            context.RequestId,
            nodeRole,
            AutomationCommandDomain.Entity,
            "lifecycleBurst",
            ActionEventPhase.Applied,
            target: $"{command.ReplacementAlias},{command.TombstoneAlias}",
            code: command.SendStaleLoads ? "stale_loads_queued" : "applied",
            data: data);

        ActionReceipt receipt = new(
            context.RequestId,
            true,
            Time.FrameIndex,
            -1,
            command.SendStaleLoads ? "stale_loads_queued" : "applied");
        return new EntityLifecycleBurstResult(
            receipt,
            CreateEntityFixture(command.ReplacementAlias, replacement),
            tombstoneEntityId,
            staleReplacementRevision,
            finalReplacementRevision,
            staleTombstoneRevision,
            finalTombstoneRevision);
    }

    private Entity ResolveEntityAlias(string alias) {
        if (!entityFixtureAliases.TryGetValue(alias, out int entityId)) {
            throw new AutomationCommandException(
                "unknown_alias",
                $"Entity fixture alias '{alias}' is not registered.");
        }
        return GameManager.Project?.FindEntity(entityId)
            ?? throw new AutomationCommandException(
                "entity_missing",
                $"Entity fixture alias '{alias}' no longer resolves to entity {entityId}.");
    }

    private static Entity CreateStableEntity(
        Project project,
        string templateName,
        Vector3 position) {
        Entity entity = DatabaseManager.CreateEntity(project, templateName, true);
        ComponentBody body = entity.FindComponent<ComponentBody>(true);
        body.Position = position;
        body.Rotation = Quaternion.Identity;
        body.Velocity = Vector3.Zero;
        body.IsGravityEnabled = false;
        body.TerrainCollidable = false;
        body.BodyCollidable = false;
        ComponentLocomotion? locomotion = entity.FindComponent<ComponentLocomotion>();
        if (locomotion != null) {
            locomotion.DisableInputMovement = true;
            locomotion.WalkOrder = null;
            locomotion.FlyOrder = null;
            locomotion.SwimOrder = null;
            locomotion.TurnOrder = Vector2.Zero;
            locomotion.JumpOrder = 0f;
        }
        ComponentHealth? health = entity.FindComponent<ComponentHealth>();
        if (health != null) {
            health.IsInvulnerable = true;
            health.m_regenerateLifeEnabled = false;
            health.Health = MathF.Max(health.Health, 1f);
            health.m_lastHealth = health.Health;
            health.CauseOfDeath = null;
            health.DeathTime = null;
        }
        ComponentSpawn? spawn = entity.FindComponent<ComponentSpawn>();
        if (spawn != null) {
            spawn.SpawnDuration = 0f;
            spawn.AutoDespawn = false;
            spawn.DespawnTime = null;
        }
        return entity;
    }

    private static EntityLifecycleFixture CreateEntityFixture(string alias, Entity entity) {
        ComponentBody body = entity.FindComponent<ComponentBody>(true);
        return new EntityLifecycleFixture(
            alias,
            entity.Id,
            entity.ValuesDictionary.DatabaseObject.Name,
            ToVector(body.Position),
            EntityLifecycleTracker.GetAuthoritativeRevision(entity.Project, entity.Id));
    }

    private void EnsureEntityServerRole(string operation) {
        if (nodeRole != "server" || !NetworkManager.IsServerRunning) {
            throw new AutomationCommandException(
                "permission_denied",
                $"{operation} must originate from the server node.");
        }
    }

    private static void ValidateEntityAlias(string alias, string fieldName) {
        if (string.IsNullOrWhiteSpace(alias)
            || alias.Length > 64
            || alias.Any(character => !char.IsAsciiLetterOrDigit(character)
                && character is not '-' and not '_')) {
            throw new AutomationCommandException(
                "invalid_payload",
                $"{fieldName} must be 1-64 ASCII letters, digits, '-' or '_'.");
        }
    }

}
