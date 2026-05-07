using Engine;
using GameEntitySystem;

namespace Game.Network;

public class FishOutOfWaterBehaviorPacket : Packet {
    public override byte ID => (byte)PacketType.BehaviorFishOutOfWater;
    public override ClientStage MinNeedState => ClientStage.Playing;
    public int m_entityId;
    public bool m_isBend;
    public FishOutOfWaterBehaviorPacket() { }
    public FishOutOfWaterBehaviorPacket(ComponentFishOutOfWaterBehavior behavior, bool isBend) {
        m_entityId = behavior.Entity.Id;
        m_isBend = isBend;
    }
    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_entityId);
        archive.Value(ref m_isBend);
    }
    public override void Handle(bool isServer) {
        if (isServer) return;
        BehaviorPacketHelpers.WithEntity(m_entityId, entity => {
            ComponentFishOutOfWaterBehavior behavior = entity.FindComponent<ComponentFishOutOfWaterBehavior>();
            if (behavior != null) {
                behavior.IsBend = m_isBend;
            }
        });
    }
}

