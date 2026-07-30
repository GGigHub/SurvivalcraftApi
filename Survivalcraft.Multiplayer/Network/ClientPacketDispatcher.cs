using Engine;

namespace Game.Network;

internal sealed class ClientPacketDispatcher {
    internal const int MaximumPendingProjectPackets = 4096;

    readonly Queue<PendingPacket> m_pendingProjectPackets = new();

    internal int PendingCount => m_pendingProjectPackets.Count;

    internal void Dispatch(ClientSession session, Packet packet) {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(packet);
        if (!packet.RequiresProject || IsProjectReady(session)) {
            packet.Handle(false);
            return;
        }
        if (m_pendingProjectPackets.Count >= MaximumPendingProjectPackets) {
            Log.Warning(
                $"Discarding deferred {packet.GetType().Name}: client project packet queue "
                + $"reached {MaximumPendingProjectPackets} entries.");
            return;
        }
        m_pendingProjectPackets.Enqueue(new PendingPacket(session.SessionId, packet));
    }

    internal void Flush(ClientSession session) {
        if (session == null || !IsProjectReady(session)) {
            return;
        }
        while (m_pendingProjectPackets.Count > 0) {
            PendingPacket pending = m_pendingProjectPackets.Dequeue();
            if (pending.SessionId == session.SessionId
                && session.AdmissionState == NetworkSessionAdmissionState.Accepted) {
                pending.Packet.Handle(false);
            }
        }
    }

    internal void Reset() => m_pendingProjectPackets.Clear();

    static bool IsProjectReady(ClientSession session) =>
        session.NetworkState >= NetworkState.ProjectLoaded
        && GameManager.Project != null;

    readonly record struct PendingPacket(Guid SessionId, Packet Packet);
}
