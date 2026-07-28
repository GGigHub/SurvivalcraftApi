using Engine;
using GameEntitySystem;

namespace Game.Network;

public class RandomFeedBehaviorPacket : Packet {
    public override byte ID => (byte)PacketType.BehaviorRandomFeed;
    public override NetworkState MinNeedState => NetworkState.Playing;
    public int m_entityId;
    public bool m_isFeed;
    public RandomFeedBehaviorPacket() { }
    public RandomFeedBehaviorPacket(ComponentRandomFeedBehavior behavior, bool isFeed) {
        m_entityId = behavior.Entity.Id;
        m_isFeed = isFeed;
    }
    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_entityId);
        archive.Value(ref m_isFeed);
    }
    public override void Handle(bool isServer) {
        if (isServer) return;
        GameManager.Project.FindEntity(m_entityId, entity => {
            ComponentRandomFeedBehavior behavior = entity.FindComponent<ComponentRandomFeedBehavior>();
            if (behavior != null) {
                behavior.IsFeed = m_isFeed;
            }
        });
    }
}

