using Engine;
using GameEntitySystem;

namespace Game.Network;

public class StubbornSteedBehaviorPacket : Packet {
    public override byte ID => (byte)PacketType.BehaviorStubbornSteed;
    public override NetworkState MinNeedState => NetworkState.Playing;
    public int m_entityId;
    public float m_headShakeOrder;

    public StubbornSteedBehaviorPacket() { }

    public StubbornSteedBehaviorPacket(ComponentStubbornSteedBehavior behavior, float headShakeOrder) {
        m_entityId = behavior.Entity.Id;
        m_headShakeOrder = headShakeOrder;
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_entityId);
        archive.Value(ref m_headShakeOrder);
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        GameManager.Project.FindEntity(m_entityId, entity => {
            ComponentCreatureModel model = entity.FindComponent<ComponentCreatureModel>();
            if (model != null) {
                model.HeadShakeOrder = m_headShakeOrder;
            }
        });
    }
}
