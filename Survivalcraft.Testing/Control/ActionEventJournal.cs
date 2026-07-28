namespace Survivalcraft.Testing.Control;

public enum ActionEventPhase {
    Queued,
    Applied,
    Rejected,
    Observed
}

public sealed record ActionEventRecord(
    long Sequence,
    DateTimeOffset Timestamp,
    long Tick,
    string ActionId,
    string Role,
    AutomationCommandDomain Domain,
    string EventName,
    ActionEventPhase Phase,
    int? ActorPlayerIndex,
    string? Target,
    string Code,
    IReadOnlyDictionary<string, string> Data);

public sealed record GetEventsCommand(
    long AfterSequence = 0,
    int Limit = 128);

public sealed record ActionEventsResult(
    ActionEventRecord[] Events,
    long OldestAvailableSequence,
    long LatestSequence,
    long DroppedEvents,
    bool Truncated);

public sealed record ResetEventsResult(long LatestSequence);

public sealed class ActionEventJournal {
    private readonly object sync = new();
    private readonly Queue<ActionEventRecord> events;
    private readonly int capacity;
    private long nextSequence;
    private long droppedEvents;

    public ActionEventJournal(int capacity = AutomationProtocol.DefaultEventJournalCapacity) {
        if (capacity <= 0) {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }
        this.capacity = capacity;
        events = new Queue<ActionEventRecord>(Math.Min(capacity, 256));
    }

    public int Capacity => capacity;

    public ActionEventRecord Record(
        long tick,
        string actionId,
        string role,
        AutomationCommandDomain domain,
        string eventName,
        ActionEventPhase phase,
        int? actorPlayerIndex = null,
        string? target = null,
        string code = "ok",
        IReadOnlyDictionary<string, string>? data = null,
        DateTimeOffset? timestamp = null) {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        lock (sync) {
            ActionEventRecord record = new(
                ++nextSequence,
                timestamp ?? DateTimeOffset.UtcNow,
                tick,
                actionId,
                role,
                domain,
                eventName,
                phase,
                actorPlayerIndex,
                target,
                code,
                CopyData(data));
            if (events.Count == capacity) {
                events.Dequeue();
                droppedEvents++;
            }
            events.Enqueue(record);
            return record;
        }
    }

    public ActionEventsResult Query(GetEventsCommand command) {
        ArgumentNullException.ThrowIfNull(command);
        if (command.AfterSequence < 0) {
            throw new AutomationCommandException(
                "invalid_payload",
                "afterSequence cannot be negative.");
        }
        if (command.Limit is < 1 or > AutomationProtocol.MaximumEventQueryCount) {
            throw new AutomationCommandException(
                "limit_exceeded",
                $"Event query limit must be between 1 and {AutomationProtocol.MaximumEventQueryCount}.");
        }

        lock (sync) {
            long oldest = events.TryPeek(out ActionEventRecord? first)
                ? first.Sequence
                : nextSequence + 1;
            bool truncated = command.AfterSequence > 0
                && command.AfterSequence < oldest - 1;
            ActionEventRecord[] selected = events
                .Where(item => item.Sequence > command.AfterSequence)
                .Take(command.Limit)
                .ToArray();
            return new ActionEventsResult(
                selected,
                oldest,
                nextSequence,
                droppedEvents,
                truncated);
        }
    }

    public ResetEventsResult Reset() {
        lock (sync) {
            events.Clear();
            droppedEvents = 0;
            return new ResetEventsResult(nextSequence);
        }
    }

    private static IReadOnlyDictionary<string, string> CopyData(
        IReadOnlyDictionary<string, string>? source) {
        SortedDictionary<string, string> result = new(StringComparer.Ordinal);
        if (source != null) {
            foreach ((string key, string value) in source) {
                result.Add(key, value);
            }
        }
        return result;
    }
}
