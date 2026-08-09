using Engine;
using GameEntitySystem;

namespace Game.Network;

public class ChaseBehaviorPacket : Packet {
    public override byte ID => (byte)PacketType.BehaviorChase;
    public override NetworkState MinNeedState => NetworkState.Playing;
    public int m_entityId;
    public bool m_isAttack;
    public long m_lifecycleRevision = -1;
    public long m_revision;

    public ChaseBehaviorPacket() { }
    public ChaseBehaviorPacket(
        ComponentChaseBehavior behavior,
        bool isAttack,
        long revision = -1) {
        m_entityId = behavior.Entity.Id;
        m_isAttack = isAttack;
        m_lifecycleRevision = NetworkEntityStatePacketHelpers.LifecycleRevision(behavior);
        m_revision = NetworkEntityStatePacketHelpers.StateRevision(
            behavior,
            NetworkStateRevisionDomain.CreatureChase,
            revision);
    }
    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_entityId);
        archive.Value(ref m_isAttack);
        archive.Value(ref m_lifecycleRevision);
        archive.Value(ref m_revision);
    }
    public override void Handle(bool isServer) {
        if (!NetworkEntityStatePacketHelpers.TryResolveComponent(
                isServer,
                m_entityId,
                m_lifecycleRevision,
                m_revision,
                NetworkStateRevisionDomain.CreatureChase,
                out ComponentChaseBehavior behavior)) {
            return;
        }
        behavior.IsAttack = m_isAttack;
    }
}

