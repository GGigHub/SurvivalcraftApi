using Engine;
using GameEntitySystem;

namespace Game.Network;

public class DigInMudBehaviorPacket : Packet {
    public override byte ID => (byte)PacketType.BehaviorDigInMud;
    public override NetworkState MinNeedState => NetworkState.Playing;
    public int m_entityId;
    public bool m_isDigIn;
    public long m_lifecycleRevision = -1;
    public long m_revision;
    public DigInMudBehaviorPacket() { }
    public DigInMudBehaviorPacket(
        ComponentDigInMudBehavior behavior,
        bool isDigIn,
        long revision = -1) {
        m_entityId = behavior.Entity.Id;
        m_isDigIn = isDigIn;
        m_lifecycleRevision = NetworkEntityStatePacketHelpers.LifecycleRevision(behavior);
        m_revision = NetworkEntityStatePacketHelpers.StateRevision(
            behavior,
            NetworkStateRevisionDomain.CreatureDigInMud,
            revision);
    }
    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_entityId);
        archive.Value(ref m_isDigIn);
        archive.Value(ref m_lifecycleRevision);
        archive.Value(ref m_revision);
    }
    public override void Handle(bool isServer) {
        if (!NetworkEntityStatePacketHelpers.TryResolveComponent(
                isServer,
                m_entityId,
                m_lifecycleRevision,
                m_revision,
                NetworkStateRevisionDomain.CreatureDigInMud,
                out ComponentDigInMudBehavior behavior)) return;
        behavior.IsDigIn = m_isDigIn;
    }
}

