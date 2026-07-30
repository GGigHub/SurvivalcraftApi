using System.Runtime.CompilerServices;
using GameEntitySystem;

namespace Game.Network;

public readonly record struct ProjectileEventMetrics(
    long GeneratedEvents,
    long AcceptedEvents,
    long IgnoredDuplicateEvents,
    long IgnoredExpiredEvents,
    long HighestGeneratedEventId,
    long HighestAcceptedEventId,
    int RememberedEventIds);

internal sealed class ProjectileEventWindow {
    internal const int RetentionWindow = 65_536;

    private readonly HashSet<long> acceptedIds = [];

    public long HighestAcceptedEventId { get; private set; }

    public long AcceptedEvents { get; private set; }

    public long IgnoredDuplicateEvents { get; private set; }

    public long IgnoredExpiredEvents { get; private set; }

    public int RememberedEventIds => acceptedIds.Count;

    public bool TryAccept(long eventId) {
        if (eventId <= 0) {
            IgnoredExpiredEvents++;
            return false;
        }
        long lowerBound = Math.Max(1, HighestAcceptedEventId - RetentionWindow + 1);
        if (eventId < lowerBound) {
            IgnoredExpiredEvents++;
            return false;
        }
        if (!acceptedIds.Add(eventId)) {
            IgnoredDuplicateEvents++;
            return false;
        }

        AcceptedEvents++;
        if (eventId > HighestAcceptedEventId) {
            HighestAcceptedEventId = eventId;
            long pruneBefore = Math.Max(1, HighestAcceptedEventId - RetentionWindow + 1);
            acceptedIds.RemoveWhere(candidate => candidate < pruneBefore);
        }
        return true;
    }
}

public static class ProjectileEventTracker {
    private static readonly ConditionalWeakTable<Project, ProjectileEventContext> Contexts = new();

    public static long NextAuthoritativeEventId(Project project) {
        ArgumentNullException.ThrowIfNull(project);
        ProjectileEventContext context = Context(project);
        context.HighestGeneratedEventId = checked(context.HighestGeneratedEventId + 1);
        context.GeneratedEvents++;
        return context.HighestGeneratedEventId;
    }

    internal static bool TryAccept(Project project, long eventId) {
        ArgumentNullException.ThrowIfNull(project);
        return Context(project).ClientWindow.TryAccept(eventId);
    }

    public static ProjectileEventMetrics GetMetrics(Project project) {
        ArgumentNullException.ThrowIfNull(project);
        ProjectileEventContext context = Context(project);
        ProjectileEventWindow window = context.ClientWindow;
        return new ProjectileEventMetrics(
            context.GeneratedEvents,
            window.AcceptedEvents,
            window.IgnoredDuplicateEvents,
            window.IgnoredExpiredEvents,
            context.HighestGeneratedEventId,
            window.HighestAcceptedEventId,
            window.RememberedEventIds);
    }

    private static ProjectileEventContext Context(Project project) =>
        Contexts.GetValue(project, static _ => new ProjectileEventContext());

    private sealed class ProjectileEventContext {
        public long GeneratedEvents;
        public long HighestGeneratedEventId;
        public ProjectileEventWindow ClientWindow { get; } = new();
    }
}
