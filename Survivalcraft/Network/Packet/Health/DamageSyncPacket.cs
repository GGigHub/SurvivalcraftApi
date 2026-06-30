using Engine;

namespace Game.Network;

public class DamageSyncPacket : Packet {
    public override byte ID => (byte)PacketType.DamageSync;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;
    public int m_targetId;
    public float m_health;

    public DamageSyncPacket() {
    }

    public DamageSyncPacket(ComponentDamage health) {
        m_targetId = health.Entity.Id;
        m_health = health.Hitpoints;
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_targetId);
        archive.Value(ref m_health);
    }

    public override void Handle(bool isServer) {
        if (isServer) return;
        GameManager.Project.FindEntity(m_targetId, e => {
            ComponentDamage h = e.FindComponent<ComponentDamage>();
            h?.Hitpoints = m_health;
        });
    }
}

