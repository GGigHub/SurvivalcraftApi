using Engine;
using GameEntitySystem;

namespace Game.Network;

public class LookAroundBehaviorPacket : Packet {
    public override byte ID => (byte)PacketType.BehaviorLookAround;
    public override NetworkState MinNeedState => NetworkState.Playing;
    public int m_entityId;
    public bool m_isLookRandom;
    public long m_lifecycleRevision = -1;
    public long m_revision;

    public LookAroundBehaviorPacket() { }

    public LookAroundBehaviorPacket(
        ComponentLookAroundBehavior behavior,
        bool isLookRandom,
        long revision = -1) {
        m_entityId = behavior.Entity.Id;
        m_isLookRandom = isLookRandom;
        m_lifecycleRevision = NetworkEntityStatePacketHelpers.LifecycleRevision(behavior);
        m_revision = NetworkEntityStatePacketHelpers.StateRevision(
            behavior,
            NetworkStateRevisionDomain.CreatureLookAround,
            revision);
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_entityId);
        archive.Value(ref m_isLookRandom);
        archive.Value(ref m_lifecycleRevision);
        archive.Value(ref m_revision);
    }

    public override void Handle(bool isServer) {
        if (!NetworkEntityStatePacketHelpers.TryResolveComponent(
                isServer,
                m_entityId,
                m_lifecycleRevision,
                m_revision,
                NetworkStateRevisionDomain.CreatureLookAround,
                out ComponentLookAroundBehavior behavior)) {
            return;
        }
        behavior.IsLookRandom = m_isLookRandom;
    }
}
