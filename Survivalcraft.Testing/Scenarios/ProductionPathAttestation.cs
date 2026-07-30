using Survivalcraft.Testing.Control;

namespace Survivalcraft.Testing.Scenarios;

public sealed record ProductionPathAttestation(
    string ActionId,
    string Role,
    string Command,
    ActionProvenance Provenance,
    string? ProductionEntry,
    string Status,
    string[] Evidence,
    string[] MissingEvidence) {
    public bool Passed => string.Equals(Status, "passed", StringComparison.Ordinal);
}

public static class ProductionPathAttestationBuilder {
    public static ProductionPathAttestation[] Build(
        IEnumerable<ScenarioActionRecordV2> actions,
        IReadOnlyDictionary<string, ActionEventsResult> eventsByRole) {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(eventsByRole);
        ActionEventRecord[] allEvents = eventsByRole.Values
            .SelectMany(result => result.Events ?? [])
            .OrderBy(item => item.Sequence)
            .ToArray();
        return actions.Select(action => BuildOne(action, allEvents)).ToArray();
    }

    public static void EnsureComplete(IEnumerable<ProductionPathAttestation> attestations) {
        ArgumentNullException.ThrowIfNull(attestations);
        ProductionPathAttestation[] failed = attestations
            .Where(attestation => !attestation.Passed)
            .ToArray();
        if (failed.Length == 0) {
            return;
        }
        throw new InvalidDataException(
            "Production path attestation failed: "
            + string.Join(
                " | ",
                failed.Select(item =>
                    $"{item.Role}/{item.Command}/{item.ActionId}: "
                    + string.Join(", ", item.MissingEvidence))));
    }

    private static ProductionPathAttestation BuildOne(
        ScenarioActionRecordV2 action,
        IReadOnlyList<ActionEventRecord> allEvents) {
        List<string> evidence = [];
        List<string> missing = [];
        ActionReceipt receipt = action.Receipt;
        if (receipt.Accepted) {
            evidence.Add("receipt.accepted");
        }
        else {
            missing.Add("accepted receipt");
        }
        if (IsActionProvenance(receipt.Provenance)) {
            evidence.Add($"receipt.provenance={receipt.Provenance}");
        }
        else {
            missing.Add("non-fixture receipt provenance");
        }
        if (!string.IsNullOrWhiteSpace(receipt.ProductionEntry)) {
            evidence.Add($"receipt.entry={receipt.ProductionEntry}");
        }
        else {
            missing.Add("production entry");
        }

        ActionEventRecord[] matching = allEvents
            .Where(item => string.Equals(
                item.ActionId,
                receipt.ActionId,
                StringComparison.Ordinal))
            .ToArray();
        ActionEventRecord? queued = matching.FirstOrDefault(item =>
            item.Phase == ActionEventPhase.Queued
            && MatchesReceiptEvidence(item, receipt));
        if (queued != null) {
            evidence.Add(
                $"event.queued={queued.Role}/{queued.Provenance}/{queued.EventName}");
        }
        else {
            missing.Add("provenance-stamped queued event");
        }
        ActionEventRecord? terminal = matching.LastOrDefault(item =>
            item.Phase is ActionEventPhase.Applied
                or ActionEventPhase.Observed
                or ActionEventPhase.Rejected
            && MatchesReceiptEvidence(item, receipt));
        if (terminal != null) {
            evidence.Add(
                $"event.terminal={terminal.Role}/{terminal.Phase}/{terminal.Provenance}/{terminal.EventName}");
        }
        else {
            missing.Add("provenance-stamped applied/observed/rejected event");
        }
        return new ProductionPathAttestation(
            receipt.ActionId,
            action.Role,
            action.Command,
            receipt.Provenance,
            receipt.ProductionEntry,
            missing.Count == 0 ? "passed" : "failed",
            evidence.ToArray(),
            missing.ToArray());
    }

    private static bool IsActionProvenance(ActionProvenance provenance) =>
        provenance is ActionProvenance.ProductionInput
            or ActionProvenance.ProductionRequest
            or ActionProvenance.AuthoritativeState
            or ActionProvenance.FaultInjection;

    private static bool MatchesReceiptEvidence(
        ActionEventRecord item,
        ActionReceipt receipt) =>
        item.Provenance == receipt.Provenance
        && string.Equals(
            item.ProductionEntry,
            receipt.ProductionEntry,
            StringComparison.Ordinal);
}
