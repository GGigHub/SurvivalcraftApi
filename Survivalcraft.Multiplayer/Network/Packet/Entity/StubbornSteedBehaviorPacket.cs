using Engine;
using GameEntitySystem;

namespace Game.Network;

public class StubbornSteedBehaviorPacket : Packet {
    public override byte ID => (byte)PacketType.BehaviorStubbornSteed;
    public override NetworkState MinNeedState => NetworkState.Playing;
    public int m_entityId;
    public float m_headShakeOrder;
    public long m_lifecycleRevision = -1;
    public long m_revision;

    public StubbornSteedBehaviorPacket() { }

    public StubbornSteedBehaviorPacket(
        ComponentStubbornSteedBehavior behavior,
        float headShakeOrder,
        long revision = -1) {
        m_entityId = behavior.Entity.Id;
        m_headShakeOrder = headShakeOrder;
        m_lifecycleRevision = NetworkEntityStatePacketHelpers.LifecycleRevision(behavior);
        m_revision = NetworkEntityStatePacketHelpers.StateRevision(
            behavior,
            NetworkStateRevisionDomain.CreatureStubbornSteed,
            revision);
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_entityId);
        archive.Value(ref m_headShakeOrder);
        archive.Value(ref m_lifecycleRevision);
        archive.Value(ref m_revision);
    }

    public override void Handle(bool isServer) {
        if (!float.IsFinite(m_headShakeOrder)
            || m_headShakeOrder is < 0f or > 8f
            || !NetworkEntityStatePacketHelpers.TryResolveComponent(
                isServer,
                m_entityId,
                m_lifecycleRevision,
                m_revision,
                NetworkStateRevisionDomain.CreatureStubbornSteed,
                out ComponentStubbornSteedBehavior behavior)) {
            return;
        }
        behavior.SetHeadShakeOrder(m_headShakeOrder, fromNetwork: true);
    }
}
