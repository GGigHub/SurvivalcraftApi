using Engine;
using GameEntitySystem;

namespace Game.Network;

public class LookAroundBehaviorPacket : Packet {
    public override byte ID => (byte)PacketType.BehaviorLookAround;
    public override NetworkState MinNeedState => NetworkState.Playing;
    public int m_entityId;
    public bool m_isLookRandom;

    public LookAroundBehaviorPacket() { }

    public LookAroundBehaviorPacket(ComponentLookAroundBehavior behavior, bool isLookRandom) {
        m_entityId = behavior.Entity.Id;
        m_isLookRandom = isLookRandom;
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_entityId);
        archive.Value(ref m_isLookRandom);
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        GameManager.Project.FindEntity(m_entityId, entity => {
            ComponentLookAroundBehavior behavior = entity.FindComponent<ComponentLookAroundBehavior>();
            if (behavior != null) {
                behavior.IsLookRandom = m_isLookRandom;
            }
        });
    }
}
