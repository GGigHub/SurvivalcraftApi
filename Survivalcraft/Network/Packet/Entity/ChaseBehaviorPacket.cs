using Engine;
using GameEntitySystem;

namespace Game.Network;

public class ChaseBehaviorPacket : Packet {
    public override byte ID => (byte)PacketType.BehaviorChase;
    public override ClientStage MinNeedState => ClientStage.Playing;
    public int m_entityId;
    public bool m_isAttack;

    public ChaseBehaviorPacket() { }
    public ChaseBehaviorPacket(ComponentChaseBehavior behavior, bool isAttack) {
        m_entityId = behavior.Entity.Id;
        m_isAttack = isAttack;
    }
    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_entityId);
        archive.Value(ref m_isAttack);
    }
    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        GameManager.Project.FindEntity(m_entityId, entity => {
            ComponentChaseBehavior behavior = entity.FindComponent<ComponentChaseBehavior>();
            if (behavior != null) {
                behavior.IsAttack = m_isAttack;
            }
        });
    }
}

