using Engine;
using GameEntitySystem;

namespace Game.Network;

public class FishOutOfWaterBehaviorPacket : Packet {
    public override byte ID => (byte)PacketType.BehaviorFishOutOfWater;
    public override NetworkState MinNeedState => NetworkState.Playing;
    public int m_entityId;
    public bool m_isBend;
    public long m_lifecycleRevision = -1;
    public long m_revision;
    public FishOutOfWaterBehaviorPacket() { }
    public FishOutOfWaterBehaviorPacket(
        ComponentFishOutOfWaterBehavior behavior,
        bool isBend,
        long revision = -1) {
        m_entityId = behavior.Entity.Id;
        m_isBend = isBend;
        m_lifecycleRevision = NetworkEntityStatePacketHelpers.LifecycleRevision(behavior);
        m_revision = NetworkEntityStatePacketHelpers.StateRevision(
            behavior,
            NetworkStateRevisionDomain.CreatureFishOutOfWater,
            revision);
    }
    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_entityId);
        archive.Value(ref m_isBend);
        archive.Value(ref m_lifecycleRevision);
        archive.Value(ref m_revision);
    }
    public override void Handle(bool isServer) {
        if (!NetworkEntityStatePacketHelpers.TryResolveComponent(
                isServer,
                m_entityId,
                m_lifecycleRevision,
                m_revision,
                NetworkStateRevisionDomain.CreatureFishOutOfWater,
                out ComponentFishOutOfWaterBehavior behavior)) return;
        behavior.IsBend = m_isBend;
    }
}

