

namespace Game.Network;

public class EntityOnFirePacket : Packet {
    public override byte ID => (byte)PacketType.EntityOnFire;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;
    public int m_entityId;
    public int m_attackerEntityId;
    public float m_duration;

    public EntityOnFirePacket() { }
    public EntityOnFirePacket(ComponentOnFire target, ComponentCreature attacker, float duration) {
        m_duration = duration;
        if (attacker != null) {
            m_attackerEntityId = attacker.Entity.Id;
        }
        m_entityId = target.Entity.Id;
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_entityId);
        archive.Value(ref m_attackerEntityId);
        archive.Value(ref m_duration);
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        GameManager.Project.FindEntity(m_entityId, e => {
            ComponentOnFire onFire = e.FindComponent<ComponentOnFire>();
            if (onFire == null) {
                return;
            }
            if (m_attackerEntityId == 0) {
                onFire.SetOnFire(null, m_duration, true);
            }
            else {
                GameManager.Project.FindEntity(m_attackerEntityId, e2 => {
                    ComponentCreature creature = e2.FindComponent<ComponentCreature>();
                    onFire.SetOnFire(creature, m_duration, true);
                });
            }
        });
    }
}

