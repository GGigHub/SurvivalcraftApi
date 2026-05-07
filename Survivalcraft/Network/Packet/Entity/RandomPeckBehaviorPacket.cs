using Engine;
using GameEntitySystem;

namespace Game.Network;

public class RandomPeckBehaviorPacket : Packet {
    public override byte ID => (byte)PacketType.BehaviorRandomPeck;
    public override ClientStage MinNeedState => ClientStage.Playing;
    public int m_entityId;
    public bool m_isFeed;
    public RandomPeckBehaviorPacket() { }
    public RandomPeckBehaviorPacket(ComponentRandomPeckBehavior behavior, bool isFeed) {
        m_entityId = behavior.Entity.Id;
        m_isFeed = isFeed;
    }
    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_entityId);
        archive.Value(ref m_isFeed);
    }
    public override void Handle(bool isServer) {
        if (isServer) return;
        BehaviorHelpers.WithEntity(m_entityId, entity => {
            ComponentRandomPeckBehavior behavior = entity.FindComponent<ComponentRandomPeckBehavior>();
            if (behavior != null) {
                behavior.IsFeed = m_isFeed;
            }
        });
    }
}

