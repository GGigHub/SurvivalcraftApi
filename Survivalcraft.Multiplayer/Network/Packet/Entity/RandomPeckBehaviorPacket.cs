using Engine;
using GameEntitySystem;

namespace Game.Network;

public class RandomPeckBehaviorPacket : Packet {
    public override byte ID => (byte)PacketType.BehaviorRandomPeck;
    public override NetworkState MinNeedState => NetworkState.Playing;
    public int m_entityId;
    public bool m_isFeed;
    public long m_lifecycleRevision = -1;
    public long m_revision;
    public RandomPeckBehaviorPacket() { }
    public RandomPeckBehaviorPacket(
        ComponentRandomPeckBehavior behavior,
        bool isFeed,
        long revision = -1) {
        m_entityId = behavior.Entity.Id;
        m_isFeed = isFeed;
        m_lifecycleRevision = NetworkEntityStatePacketHelpers.LifecycleRevision(behavior);
        m_revision = NetworkEntityStatePacketHelpers.StateRevision(
            behavior,
            NetworkStateRevisionDomain.CreatureRandomPeck,
            revision);
    }
    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_entityId);
        archive.Value(ref m_isFeed);
        archive.Value(ref m_lifecycleRevision);
        archive.Value(ref m_revision);
    }
    public override void Handle(bool isServer) {
        if (!NetworkEntityStatePacketHelpers.TryResolveComponent(
                isServer,
                m_entityId,
                m_lifecycleRevision,
                m_revision,
                NetworkStateRevisionDomain.CreatureRandomPeck,
                out ComponentRandomPeckBehavior behavior)) return;
        behavior.IsFeed = m_isFeed;
    }
}

