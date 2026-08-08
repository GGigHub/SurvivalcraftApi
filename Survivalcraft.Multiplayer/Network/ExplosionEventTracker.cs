using System.Runtime.CompilerServices;
using GameEntitySystem;

namespace Game.Network;

public readonly record struct ExplosionEventMetrics(
    long GeneratedEvents,
    long GeneratedCellChunks,
    long GeneratedCells,
    long GeneratedSoundEvents,
    long AcceptedEvents,
    long AcceptedCellChunks,
    long AppliedCells,
    long AcceptedSoundEvents,
    long IgnoredDuplicateParts,
    long IgnoredExpiredParts,
    long HighestGeneratedEventId,
    long HighestAcceptedEventId,
    int RememberedEvents);

internal sealed class ExplosionEventWindow {
    internal const int RetentionWindow = 65_536;

    private readonly Dictionary<long, AcceptedParts> accepted = [];

    public long AcceptedEvents { get; private set; }
    public long AcceptedCellChunks { get; private set; }
    public long AppliedCells { get; private set; }
    public long AcceptedSoundEvents { get; private set; }
    public long IgnoredDuplicateParts { get; private set; }
    public long IgnoredExpiredParts { get; private set; }
    public long HighestAcceptedEventId { get; private set; }
    public int RememberedEvents => accepted.Count;

    public bool TryAcceptCells(
        long eventId,
        int chunkIndex,
        int chunkCount,
        int cellCount) {
        if (chunkCount is <= 0 or > ExplosionPacket.MaximumChunksPerEvent
            || chunkIndex < 0
            || chunkIndex >= chunkCount
            || cellCount is <= 0 or > ExplosionPacket.MaximumCellsPerChunk
            || !TryGetParts(eventId, out AcceptedParts parts)) {
            return false;
        }
        if (parts.CellChunkCount != 0 && parts.CellChunkCount != chunkCount
            || !parts.CellChunks.Add(chunkIndex)) {
            IgnoredDuplicateParts++;
            return false;
        }
        parts.CellChunkCount = chunkCount;
        AcceptedCellChunks++;
        AppliedCells += cellCount;
        return true;
    }

    public bool TryAcceptSound(long eventId) {
        if (!TryGetParts(eventId, out AcceptedParts parts)) {
            return false;
        }
        if (parts.SoundAccepted) {
            IgnoredDuplicateParts++;
            return false;
        }
        parts.SoundAccepted = true;
        AcceptedSoundEvents++;
        return true;
    }

    private bool TryGetParts(long eventId, out AcceptedParts parts) {
        parts = null;
        if (eventId <= 0) {
            IgnoredExpiredParts++;
            return false;
        }
        long lowerBound = Math.Max(1, HighestAcceptedEventId - RetentionWindow + 1);
        if (eventId < lowerBound) {
            IgnoredExpiredParts++;
            return false;
        }
        if (!accepted.TryGetValue(eventId, out parts)) {
            parts = new AcceptedParts();
            accepted.Add(eventId, parts);
            AcceptedEvents++;
        }
        if (eventId > HighestAcceptedEventId) {
            HighestAcceptedEventId = eventId;
            long pruneBefore = Math.Max(1, HighestAcceptedEventId - RetentionWindow + 1);
            foreach (long expired in accepted.Keys.Where(candidate => candidate < pruneBefore).ToArray()) {
                accepted.Remove(expired);
            }
        }
        return true;
    }

    private sealed class AcceptedParts {
        public int CellChunkCount;
        public HashSet<int> CellChunks { get; } = [];
        public bool SoundAccepted;
    }
}

public static class ExplosionEventTracker {
    private static readonly ConditionalWeakTable<Project, ExplosionEventContext> Contexts = new();

    public static void RecordGeneratedCells(
        Project project,
        long eventId,
        int chunkCount,
        int cellCount) {
        ArgumentNullException.ThrowIfNull(project);
        if (eventId <= 0
            || chunkCount is <= 0 or > ExplosionPacket.MaximumChunksPerEvent
            || cellCount is <= 0 or > ExplosionPacket.MaximumCellsPerEvent) {
            return;
        }
        ExplosionEventContext context = Context(project);
        RecordGeneratedEvent(context, eventId);
        if (context.GeneratedCellEvents.Add(eventId)) {
            context.GeneratedCellChunks += chunkCount;
            context.GeneratedCells += cellCount;
        }
        PruneGenerated(context, eventId);
    }

    public static void RecordGeneratedSound(Project project, long eventId) {
        ArgumentNullException.ThrowIfNull(project);
        if (eventId <= 0) {
            return;
        }
        ExplosionEventContext context = Context(project);
        RecordGeneratedEvent(context, eventId);
        if (context.GeneratedSoundEvents.Add(eventId)) {
            context.GeneratedSoundEventCount++;
        }
        PruneGenerated(context, eventId);
    }

    internal static bool TryAcceptCells(
        Project project,
        long eventId,
        int chunkIndex,
        int chunkCount,
        int cellCount) {
        ArgumentNullException.ThrowIfNull(project);
        return Context(project).ClientWindow.TryAcceptCells(
            eventId,
            chunkIndex,
            chunkCount,
            cellCount);
    }

    internal static bool TryAcceptSound(Project project, long eventId) {
        ArgumentNullException.ThrowIfNull(project);
        return Context(project).ClientWindow.TryAcceptSound(eventId);
    }

    public static ExplosionEventMetrics GetMetrics(Project project) {
        ArgumentNullException.ThrowIfNull(project);
        ExplosionEventContext context = Context(project);
        ExplosionEventWindow window = context.ClientWindow;
        return new ExplosionEventMetrics(
            context.GeneratedEvents,
            context.GeneratedCellChunks,
            context.GeneratedCells,
            context.GeneratedSoundEventCount,
            window.AcceptedEvents,
            window.AcceptedCellChunks,
            window.AppliedCells,
            window.AcceptedSoundEvents,
            window.IgnoredDuplicateParts,
            window.IgnoredExpiredParts,
            context.HighestGeneratedEventId,
            window.HighestAcceptedEventId,
            window.RememberedEvents);
    }

    private static void RecordGeneratedEvent(ExplosionEventContext context, long eventId) {
        if (eventId <= 0) {
            return;
        }
        if (context.GeneratedEventsSeen.Add(eventId)) {
            context.GeneratedEvents++;
        }
        context.HighestGeneratedEventId = Math.Max(context.HighestGeneratedEventId, eventId);
    }

    private static void PruneGenerated(ExplosionEventContext context, long eventId) {
        long pruneBefore = Math.Max(1, eventId - ExplosionEventWindow.RetentionWindow + 1);
        context.GeneratedEventsSeen.RemoveWhere(candidate => candidate < pruneBefore);
        context.GeneratedCellEvents.RemoveWhere(candidate => candidate < pruneBefore);
        context.GeneratedSoundEvents.RemoveWhere(candidate => candidate < pruneBefore);
    }

    private static ExplosionEventContext Context(Project project) =>
        Contexts.GetValue(project, static _ => new ExplosionEventContext());

    private sealed class ExplosionEventContext {
        public HashSet<long> GeneratedEventsSeen { get; } = [];
        public HashSet<long> GeneratedCellEvents { get; } = [];
        public HashSet<long> GeneratedSoundEvents { get; } = [];
        public long GeneratedEvents;
        public long GeneratedCellChunks;
        public long GeneratedCells;
        public long GeneratedSoundEventCount;
        public long HighestGeneratedEventId;
        public ExplosionEventWindow ClientWindow { get; } = new();
    }
}
