using Engine;

namespace Game.Network;

public class InjureEventPacket : Packet {
    public override byte ID => (byte)PacketType.InjureEvent;
    public override ClientState MinNeedState => ClientState.ProjectLoaded;
    public int m_targetId;
    public int m_attackerId;
    public float m_amount;
    public float m_health;
    public string m_cause = "";
    public bool m_ignoreInvulnerability;

    public InjureEventPacket() {
    }

    public InjureEventPacket(ComponentHealth target, ComponentCreature attacker, float amount, string cause, bool ignoreInvulnerability) {
        m_targetId = target.Entity.Id;
        m_attackerId = attacker?.Entity.Id ?? 0;
        m_amount = amount;
        m_health = target.Health;
        m_cause = cause;
        m_ignoreInvulnerability = ignoreInvulnerability;
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_targetId);
        archive.Value(ref m_attackerId);
        archive.Value(ref m_health);
        archive.Value(ref m_amount);
        archive.Value(ref m_cause);
        archive.Value(ref m_ignoreInvulnerability);
    }

    public override void Handle(bool isServer) {
        if (isServer) return;
        GameManager.Project.FindEntity(m_targetId, entity => {
            var health = entity.FindComponent<ComponentHealth>();
            if (health == null) return;
            if (m_attackerId == 0) {
                health.NetInjure(m_amount, null, m_cause);
                health.Health = m_health;
            }
            else {
                GameManager.Project.FindEntity(m_attackerId, entity2 => {
                    var attacker = entity2.FindComponent<ComponentCreature>();
                    health.NetInjure(m_amount, attacker, m_cause);
                    health.Health = m_health;
                });
            }
        });
    }
}

