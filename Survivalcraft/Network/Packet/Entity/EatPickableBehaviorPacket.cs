using Engine;
using GameEntitySystem;

namespace Game.Network;

public class EatPickableBehaviorPacket : Packet {
    public override byte ID => (byte)PacketType.BehaviorEatPickable;
    public override ClientStage MinNeedState => ClientStage.Playing;
    public int m_entityId;
    public bool m_isFeed;
    public EatPickableBehaviorPacket() { }
    public EatPickableBehaviorPacket(ComponentEatPickableBehavior behavior, bool isFeed) {
        m_entityId = behavior.Entity.Id;
        m_isFeed = isFeed;
    }
    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_entityId);
        archive.Value(ref m_isFeed);
    }
    public override void Handle(bool isServer) {
        if (isServer) return;
        BehaviorPacketHelpers.WithEntity(m_entityId, entity => {
            ComponentEatPickableBehavior behavior = entity.FindComponent<ComponentEatPickableBehavior>();
            if (behavior != null) {
                behavior.IsFeed = m_isFeed;
            }
        });
    }
}

