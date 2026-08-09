using System.Globalization;
using Engine;
using Game;
using Game.Network;
using GameEntitySystem;
using Survivalcraft.Testing.Control;

namespace Survivalcraft.TestAutomation;

public partial class TestAutomationModLoader {
    private const int CreativeFlyObservationDeadlineFrames = 1200;

    private PendingCreativeFlyAction? pendingCreativeFlyAction;

    private sealed record PendingCreativeFlyAction(
        string ActionId,
        long NetworkRequestId,
        int PlayerIndex,
        int EntityId,
        bool RequestedEnabled,
        bool PreviousEnabled,
        CreativeFlyExpectedResult ExpectedResult,
        int DuplicateCount,
        long DeadlineFrame,
        ActionProvenance Provenance);

    private ActionReceipt SetCreativeFly(
        AutomationCommandContext context,
        SetCreativeFlyCommand command) {
        if (!Enum.IsDefined(command.ExpectedResult)
            || command.DuplicateCount is < 1 or > 16) {
            throw new AutomationCommandException(
                "invalid_payload",
                "Creative-fly expectedResult must be known and duplicateCount must be between 1 and 16.");
        }
        bool isAuthoritativeServer = string.Equals(nodeRole, "server", StringComparison.Ordinal)
            && NetworkManager.IsServerRunning;
        if (!isAuthoritativeServer && !NetworkManager.IsClientRunning) {
            throw new AutomationCommandException(
                "permission_denied",
                "Creative-fly gameplay actions require the authoritative server or a connected client node.");
        }
        EnsureNoTypedPlayerActionInProgress();
        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
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

        ComponentPlayer componentPlayer = actor.ComponentPlayer;
        bool previous = componentPlayer.ComponentLocomotion.IsCreativeFlyEnabled;
        if (previous == command.Enabled) {
            throw new AutomationCommandException(
                "invalid_state",
                "The requested creative-fly state already matches the local player; no production request would be emitted.");
        }

        if (isAuthoritativeServer) {
            return SetAuthoritativeServerCreativeFly(
                context,
                command,
                componentPlayer,
                previous);
        }

        long reservationBefore = CreativeFlyResultTracker.LastReservedRequestId;
        if (!componentPlayer.ComponentGui.TrySetCreativeFlyEnabled(command.Enabled)) {
            throw new AutomationCommandException(
                "action_rejected",
                "The production gameplay path rejected the creative-fly input before it reached the network.");
        }
        long networkRequestId = CreativeFlyResultTracker.LastReservedRequestId;
        if (networkRequestId <= 0 || networkRequestId == reservationBefore) {
            componentPlayer.ComponentLocomotion.IsCreativeFlyEnabled = previous;
            throw new AutomationCommandException(
                "production_path_not_observed",
                "The production creative-fly hook did not reserve a network request identity.");
        }

        ActionProvenance provenance = command.ExpectedResult == CreativeFlyExpectedResult.Accepted
            ? ActionProvenance.ProductionInput
            : ActionProvenance.FaultInjection;
        pendingCreativeFlyAction = new PendingCreativeFlyAction(
            context.RequestId,
            networkRequestId,
            actor.PlayerIndex,
            componentPlayer.Entity.Id,
            command.Enabled,
            previous,
            command.ExpectedResult,
            command.DuplicateCount,
            checked(Time.FrameIndex + CreativeFlyObservationDeadlineFrames),
            provenance);
        RecordCreativeFlyAction(
            pendingCreativeFlyAction,
            ActionEventPhase.Queued,
            "production_request_queued");

        for (int index = 1; index < command.DuplicateCount; index++) {
            QueueFaultInjection(new PlayerCreativeFlyPacket(componentPlayer, networkRequestId));
        }
        ScheduleCreativeFlyObservation(pendingCreativeFlyAction);
        return new ActionReceipt(
            context.RequestId,
            true,
            Time.FrameIndex,
            actor.PlayerIndex,
            "queued",
            Provenance: provenance,
            ProductionEntry: ProductionPathEntries.CreativeFly);
    }

    private ActionReceipt SetAuthoritativeServerCreativeFly(
        AutomationCommandContext context,
        SetCreativeFlyCommand command,
        ComponentPlayer componentPlayer,
        bool previous) {
        if (command.ExpectedResult != CreativeFlyExpectedResult.Accepted
            || command.DuplicateCount != 1) {
            throw new AutomationCommandException(
                "invalid_payload",
                "Authoritative server creative-fly actions require Accepted and duplicateCount=1.");
        }

        PendingCreativeFlyAction evidence = new(
            context.RequestId,
            NetworkRequestId: 0,
            componentPlayer.PlayerData.PlayerIndex,
            componentPlayer.Entity.Id,
            command.Enabled,
            previous,
            command.ExpectedResult,
            command.DuplicateCount,
            DeadlineFrame: Time.FrameIndex,
            ActionProvenance.ProductionInput);
        RecordCreativeFlyAction(
            evidence,
            ActionEventPhase.Queued,
            "production_request_queued");

        long revisionBefore = NetworkStateRevisionTracker.GetAuthoritativeRevision(
            componentPlayer.Entity.Project,
            NetworkStateRevisionDomain.CreativeFly,
            componentPlayer.Entity.Id);
        if (!componentPlayer.ComponentGui.TrySetCreativeFlyEnabled(command.Enabled)) {
            RecordCreativeFlyAction(
                evidence,
                ActionEventPhase.Rejected,
                "action_rejected");
            throw new AutomationCommandException(
                "action_rejected",
                "The authoritative production gameplay path rejected the creative-fly input.");
        }
        long revision = NetworkStateRevisionTracker.GetAuthoritativeRevision(
            componentPlayer.Entity.Project,
            NetworkStateRevisionDomain.CreativeFly,
            componentPlayer.Entity.Id);
        if (revision <= revisionBefore) {
            RecordCreativeFlyAction(
                evidence,
                ActionEventPhase.Rejected,
                "production_path_not_observed");
            throw new AutomationCommandException(
                "production_path_not_observed",
                "The server creative-fly hook did not advance its authoritative state revision.");
        }

        RecordCreativeFlyAction(
            evidence,
            ActionEventPhase.Applied,
            "authoritative_result_accepted",
            new NetworkCreativeFlyResult(
                RequestId: 0,
                CreativeFlyResultCode.Accepted,
                componentPlayer.PlayerData.PlayerIndex,
                componentPlayer.Entity.Id,
                command.Enabled,
                revision));
        return new ActionReceipt(
            context.RequestId,
            true,
            Time.FrameIndex,
            componentPlayer.PlayerData.PlayerIndex,
            "applied",
            Provenance: ActionProvenance.ProductionInput,
            ProductionEntry: ProductionPathEntries.CreativeFly);
    }

    private void ScheduleCreativeFlyObservation(PendingCreativeFlyAction pending) {
        Time.QueueFrameIndexDelayedExecution(checked(Time.FrameIndex + 1), () => {
            if (pendingCreativeFlyAction?.ActionId != pending.ActionId) {
                return;
            }
            PendingCreativeFlyAction current = pendingCreativeFlyAction;
            if (CreativeFlyResultTracker.TryTake(
                    current.NetworkRequestId,
                    out NetworkCreativeFlyResult result)) {
                CreativeFlyResultCode expected = ToProductionResult(current.ExpectedResult);
                bool expectedEnabled = expected is CreativeFlyResultCode.Accepted
                    or CreativeFlyResultCode.AlreadyApplied
                        ? current.RequestedEnabled
                        : current.PreviousEnabled;
                bool matches = result.Code == expected
                    && result.PlayerIndex == current.PlayerIndex
                    && result.EntityId == current.EntityId
                    && result.Enabled == expectedEnabled;
                RecordCreativeFlyAction(
                    current,
                    matches ? ActionEventPhase.Applied : ActionEventPhase.Rejected,
                    matches
                        ? $"authoritative_result_{result.Code.ToString().ToLowerInvariant()}"
                        : $"unexpected_result_{result.Code.ToString().ToLowerInvariant()}",
                    result);
                pendingCreativeFlyAction = null;
                return;
            }
            if (Time.FrameIndex >= current.DeadlineFrame) {
                RecordCreativeFlyAction(
                    current,
                    ActionEventPhase.Rejected,
                    "authoritative_result_not_observed");
                pendingCreativeFlyAction = null;
                return;
            }
            ScheduleCreativeFlyObservation(current);
        });
    }

    private CreativeFlyRevisionBurstResult RunCreativeFlyRevisionBurst(
        AutomationCommandContext context,
        RunCreativeFlyRevisionBurstCommand command) {
        if (nodeRole != "server" || !NetworkManager.IsServerRunning) {
            throw new AutomationCommandException(
                "permission_denied",
                "Creative-fly revision bursts must originate from the server node.");
        }
        if (command.IntermediateEnabled == command.FinalEnabled) {
            throw new AutomationCommandException(
                "invalid_payload",
                "Creative-fly revision burst states must be different.");
        }
        Project project = GameManager.Project
            ?? throw new AutomationCommandException("invalid_state", "No project is loaded.");
        ComponentPlayer player = ResolvePlayer(
            project.FindSubsystem<SubsystemPlayers>(true),
            command.PlayerIndex,
            requireMainPlayer: false);
        if (player.PlayerData.IsMainPlayer || player.Entity.Id <= 0) {
            throw new AutomationCommandException(
                "invalid_actor",
                "Creative-fly revision bursts require a connected remote player.");
        }

        actionEventJournal.Record(
            Time.FrameIndex,
            context.RequestId,
            nodeRole,
            AutomationCommandDomain.Player,
            "creativeFlyRevisionBurst",
            ActionEventPhase.Queued,
            command.PlayerIndex,
            player.Entity.Id.ToString(CultureInfo.InvariantCulture),
            "queued",
            provenance: ActionProvenance.FaultInjection,
            productionEntry: ProductionPathEntries.CreativeFlyState);

        player.ComponentLocomotion.IsCreativeFlyEnabled = command.IntermediateEnabled;
        long intermediateRevision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
            project,
            NetworkStateRevisionDomain.CreativeFly,
            player.Entity.Id);
        PlayerCreativeFlyPacket stale = PlayerCreativeFlyPacket.CreateState(
            player,
            command.IntermediateEnabled,
            revision: intermediateRevision);

        player.ComponentLocomotion.IsCreativeFlyEnabled = command.FinalEnabled;
        long finalRevision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
            project,
            NetworkStateRevisionDomain.CreativeFly,
            player.Entity.Id);
        QueueAuthoritativeState(PlayerCreativeFlyPacket.CreateState(
            player,
            command.FinalEnabled,
            revision: finalRevision));
        if (command.SendStaleState) {
            QueueFaultInjectionAfterSettle(stale);
        }

        actionEventJournal.Record(
            Time.FrameIndex,
            context.RequestId,
            nodeRole,
            AutomationCommandDomain.Player,
            "creativeFlyRevisionBurst",
            ActionEventPhase.Applied,
            command.PlayerIndex,
            player.Entity.Id.ToString(CultureInfo.InvariantCulture),
            command.SendStaleState ? "stale_state_queued" : "final_state_queued",
            data: new Dictionary<string, string> {
                ["intermediateRevision"] = intermediateRevision.ToString(CultureInfo.InvariantCulture),
                ["finalRevision"] = finalRevision.ToString(CultureInfo.InvariantCulture)
            },
            provenance: ActionProvenance.FaultInjection,
            productionEntry: ProductionPathEntries.CreativeFlyState);
        ActionReceipt receipt = new(
            context.RequestId,
            true,
            Time.FrameIndex,
            command.PlayerIndex,
            command.SendStaleState ? "stale_state_queued" : "applied",
            Provenance: ActionProvenance.FaultInjection,
            ProductionEntry: ProductionPathEntries.CreativeFlyState);
        return new CreativeFlyRevisionBurstResult(
            receipt,
            command.PlayerIndex,
            player.Entity.Id,
            command.IntermediateEnabled,
            command.FinalEnabled,
            intermediateRevision,
            finalRevision);
    }

    private void RecordCreativeFlyAction(
        PendingCreativeFlyAction pending,
        ActionEventPhase phase,
        string code,
        NetworkCreativeFlyResult? result = null) {
        Dictionary<string, string> data = new(StringComparer.Ordinal) {
            ["networkRequestId"] = pending.NetworkRequestId.ToString(CultureInfo.InvariantCulture),
            ["requestedEnabled"] = pending.RequestedEnabled.ToString(CultureInfo.InvariantCulture),
            ["previousEnabled"] = pending.PreviousEnabled.ToString(CultureInfo.InvariantCulture),
            ["expectedResult"] = pending.ExpectedResult.ToString(),
            ["duplicateCount"] = pending.DuplicateCount.ToString(CultureInfo.InvariantCulture)
        };
        if (result.HasValue) {
            data["result"] = result.Value.Code.ToString();
            data["authoritativeEnabled"] = result.Value.Enabled.ToString(CultureInfo.InvariantCulture);
            data["revision"] = result.Value.Revision.ToString(CultureInfo.InvariantCulture);
        }
        actionEventJournal.Record(
            Time.FrameIndex,
            pending.ActionId,
            nodeRole,
            AutomationCommandDomain.Player,
            "creativeFly",
            phase,
            pending.PlayerIndex,
            pending.EntityId.ToString(CultureInfo.InvariantCulture),
            code,
            data,
            provenance: pending.Provenance,
            productionEntry: ProductionPathEntries.CreativeFly);
    }

    private static CreativeFlyResultCode ToProductionResult(CreativeFlyExpectedResult expected) =>
        expected switch {
            CreativeFlyExpectedResult.Accepted => CreativeFlyResultCode.Accepted,
            CreativeFlyExpectedResult.AlreadyApplied => CreativeFlyResultCode.AlreadyApplied,
            CreativeFlyExpectedResult.InvalidRequest => CreativeFlyResultCode.InvalidRequest,
            CreativeFlyExpectedResult.InvalidGameMode => CreativeFlyResultCode.InvalidGameMode,
            CreativeFlyExpectedResult.Mounted => CreativeFlyResultCode.Mounted,
            CreativeFlyExpectedResult.StaleRequest => CreativeFlyResultCode.StaleRequest,
            _ => throw new ArgumentOutOfRangeException(nameof(expected))
        };

    private void ResetCreativeFlyActions() {
        pendingCreativeFlyAction = null;
        CreativeFlyResultTracker.Reset();
    }
}
