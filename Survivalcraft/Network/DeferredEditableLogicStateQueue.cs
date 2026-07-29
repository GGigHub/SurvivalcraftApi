using System.Runtime.CompilerServices;
using GameEntitySystem;

namespace Game.Network;

/// <summary>
/// Holds authoritative editable-block states that arrive before their terrain
/// chunk. Entries are keyed by logical target, so a delayed older state cannot
/// displace a newer baseline and memory remains bounded by the number of
/// distinct editable targets announced by the server.
/// </summary>
internal static class DeferredEditableLogicStateQueue {
    internal const int MaximumPendingStates = 4096;

    private static readonly ConditionalWeakTable<Project, PendingContext> Contexts = new();

    internal static void Enqueue(
        Project project,
        NetworkLogicStateKey key,
        EditableBlockPacketBase packet) {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(packet);
        if (key.Domain != NetworkLogicStateDomain.Editable
            || key.IsItem
            || packet.m_packetKind != EditableLogicPacketKind.State
            || packet.m_revision < 0
            || packet.m_terrainRevision < 0) {
            return;
        }

        PendingContext context = Contexts.GetValue(project, _ => new PendingContext());
        lock (context.SyncRoot) {
            if (context.States.TryGetValue(key, out PendingState existing)) {
                if (existing.Packet.m_revision > packet.m_revision
                    || existing.Packet.m_revision == packet.m_revision
                        && existing.Packet.m_terrainRevision > packet.m_terrainRevision) {
                    return;
                }
                existing.Packet = packet;
                context.Order.Remove(existing.Node);
                context.Order.AddLast(existing.Node);
                return;
            }
            while (context.States.Count >= MaximumPendingStates) {
                LinkedListNode<NetworkLogicStateKey> oldest = context.Order.First;
                if (oldest == null) {
                    context.States.Clear();
                    break;
                }
                context.Order.RemoveFirst();
                context.States.Remove(oldest.Value);
            }
            LinkedListNode<NetworkLogicStateKey> node = context.Order.AddLast(key);
            context.States[key] = new PendingState(packet, node);
        }
    }

    internal static void ApplyChunk(Project project, int chunkX, int chunkZ) {
        ArgumentNullException.ThrowIfNull(project);
        if (!Contexts.TryGetValue(project, out PendingContext context)) {
            return;
        }

        EditableBlockPacketBase[] ready;
        lock (context.SyncRoot) {
            NetworkLogicStateKey[] keys = context.States.Keys
                .Where(key => key.Point.X >> 4 == chunkX && key.Point.Z >> 4 == chunkZ)
                .ToArray();
            ready = keys.Select(key => context.States[key].Packet).ToArray();
            foreach (NetworkLogicStateKey key in keys) {
                context.Order.Remove(context.States[key].Node);
                context.States.Remove(key);
            }
        }

        foreach (EditableBlockPacketBase packet in ready
                     .OrderBy(item => item.m_revision)
                     .ThenBy(item => item.m_terrainRevision)) {
            packet.Handle(isServer: false);
        }
    }

    internal static int GetPendingCount(Project project) {
        ArgumentNullException.ThrowIfNull(project);
        if (!Contexts.TryGetValue(project, out PendingContext context)) {
            return 0;
        }
        lock (context.SyncRoot) {
            return context.States.Count;
        }
    }

    private sealed class PendingContext {
        internal object SyncRoot { get; } = new();
        internal Dictionary<NetworkLogicStateKey, PendingState> States { get; } = [];
        internal LinkedList<NetworkLogicStateKey> Order { get; } = [];
    }

    private sealed class PendingState(
        EditableBlockPacketBase packet,
        LinkedListNode<NetworkLogicStateKey> node) {
        internal EditableBlockPacketBase Packet { get; set; } = packet;
        internal LinkedListNode<NetworkLogicStateKey> Node { get; } = node;
    }
}
