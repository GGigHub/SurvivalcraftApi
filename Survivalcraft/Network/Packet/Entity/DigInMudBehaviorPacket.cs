using Engine;
using GameEntitySystem;

namespace Game.Network;

public class DigInMudBehaviorPacket : Packet {
    public override byte ID => (byte)PacketType.BehaviorDigInMud;
    public override ClientStage MinNeedState => ClientStage.Playing;
    public int m_entityId;
    public bool m_isDigIn;
    public DigInMudBehaviorPacket() { }
    public DigInMudBehaviorPacket(ComponentDigInMudBehavior behavior, bool isDigIn) {
        m_entityId = behavior.Entity.Id;
        m_isDigIn = isDigIn;
    }
    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_entityId);
        archive.Value(ref m_isDigIn);
    }
    public override void Handle(bool isServer) {
        if (isServer) return;
        BehaviorHelpers.WithEntity(m_entityId, entity => {
            ComponentDigInMudBehavior behavior = entity.FindComponent<ComponentDigInMudBehavior>();
            if (behavior != null) {
                behavior.IsDigIn = m_isDigIn;
            }
        });
    }
}

