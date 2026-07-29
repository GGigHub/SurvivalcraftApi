namespace Survivalcraft.Testing.Control;

public static class AutomationProtocol {
    public const int CurrentApiVersion = 6;
    public const int CurrentSnapshotSchemaVersion = 2;
    public const int MaximumSnapshotEntities = 2048;
    public const int MaximumSnapshotTerrainCells = 4096;
    // A single creative inventory exposes more than 1600 catalog slots.
    public const int MaximumSnapshotInventorySlots = 4096;
    public const int MaximumSnapshotLogicElements = 1024;
    public const int MaximumEventQueryCount = 512;
    public const int DefaultEventJournalCapacity = 4096;
}

public enum AutomationCommandDomain {
    System,
    Fixture,
    Node,
    World,
    Network,
    Player,
    Terrain,
    Entity,
    Inventory,
    Health,
    Environment,
    Mechanics,
    Logic,
    State,
    Time,
    Ui,
    Legacy
}

public enum AutomationCommandStability {
    Stable,
    Experimental,
    Legacy
}

public sealed record AutomationCommandDescriptor(
    string Name,
    AutomationCommandDomain Domain,
    AutomationCommandStability Stability,
    bool MutatesState,
    string[] AllowedRoles,
    bool RequiresProject = false,
    bool RequiresNetwork = false);

public sealed record AutomationLimits(
    int MaximumFrameBytes,
    int MaximumSnapshotEntities,
    int MaximumSnapshotTerrainCells,
    int MaximumSnapshotInventorySlots,
    int MaximumSnapshotLogicElements,
    int MaximumEventsPerQuery,
    int EventJournalCapacity);

public sealed record AutomationDescription(
    int WireProtocolVersion,
    int AutomationApiVersion,
    int[] SnapshotSchemaVersions,
    AutomationCommandDescriptor[] SupportedCommands,
    string[] SupportedDomains,
    AutomationLimits Limits,
    string BuildIdentity,
    bool LegacyExecutionEnabled);

public sealed record RequiredAutomationCapabilities(
    int MinimumAutomationApiVersion = AutomationProtocol.CurrentApiVersion,
    int? SnapshotSchemaVersion = null,
    string[]? Commands = null,
    AutomationCommandDomain[]? Domains = null,
    bool AllowLegacyExecution = true);

public static class AutomationCapabilityValidator {
    public static void EnsureSupported(
        AutomationDescription description,
        RequiredAutomationCapabilities required) {
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(required);

        List<string> missing = [];
        if (description.WireProtocolVersion != ControlProtocol.CurrentVersion) {
            missing.Add(
                $"wireProtocol={ControlProtocol.CurrentVersion} (actual {description.WireProtocolVersion})");
        }
        if (description.AutomationApiVersion < required.MinimumAutomationApiVersion) {
            missing.Add(
                $"automationApi>={required.MinimumAutomationApiVersion} (actual {description.AutomationApiVersion})");
        }
        if (required.SnapshotSchemaVersion is int snapshotVersion
            && !description.SnapshotSchemaVersions.Contains(snapshotVersion)) {
            missing.Add($"snapshotSchema={snapshotVersion}");
        }

        HashSet<string> commands = description.SupportedCommands
            .Select(command => command.Name)
            .ToHashSet(StringComparer.Ordinal);
        foreach (string command in required.Commands ?? []) {
            if (!commands.Contains(command)) {
                missing.Add($"command={command}");
            }
        }

        HashSet<AutomationCommandDomain> domains = description.SupportedCommands
            .Select(command => command.Domain)
            .ToHashSet();
        foreach (AutomationCommandDomain domain in required.Domains ?? []) {
            if (!domains.Contains(domain)) {
                missing.Add($"domain={domain}");
            }
        }
        if (!required.AllowLegacyExecution && description.LegacyExecutionEnabled) {
            missing.Add("legacyExecution=false");
        }

        if (missing.Count > 0) {
            throw new ControlProtocolException(
                "unsupported_capability",
                "Automation runtime is missing required capabilities: "
                + string.Join(", ", missing));
        }
    }
}

public sealed record AutomationCommandContext(
    string RequestId,
    long ResponseSequence,
    string NodeRole);

public sealed class AutomationCommandException : Exception {
    public AutomationCommandException(string code, string message, string? details = null)
        : base(message) {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code;
        Details = details;
    }

    public string Code { get; }
    public string? Details { get; }
}

public sealed record ActionReceipt(
    string ActionId,
    bool Accepted,
    long QueuedTick,
    int ActorPlayerIndex,
    string Code,
    string? Detail = null);

public sealed record SetCreativeFlyCommand(bool Enabled, int? PlayerIndex = null);
