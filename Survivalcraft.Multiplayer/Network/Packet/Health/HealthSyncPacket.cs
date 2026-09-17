using Engine;

namespace Game.Network;

public class HealthSyncPacket : Packet {
    public override byte ID => (byte)PacketType.HealthSync;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;
    public int m_targetId;
    public float m_health;
    public string m_cause = "";

    public HealthSyncPacket() {
    }

    public HealthSyncPacket(ComponentHealth health) {
        m_targetId = health.Entity.Id;
        m_health = health.Health;
        m_cause = health.CauseOfDeath ?? "";
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_targetId);
        archive.Value(ref m_health);
        archive.Value(ref m_cause);
    }

    public override void Handle(bool isServer) {
        if (isServer) return;
        GameManager.Project.FindEntity(m_targetId, e => {
            ComponentHealth health = e.FindComponent<ComponentHealth>();
            if (health != null) {
                if (PlayerPacketBase.ShouldApplyCreatureHealthSync?.Invoke(m_targetId) == false) {
                    return;
                }
                ComponentPlayer player = e.FindComponent<ComponentPlayer>();
                if (player?.PlayerData != null
                    && PlayerPacketBase.ShouldApplyHealthSync?.Invoke(player.PlayerData.PlayerIndex) == false) {
                    return;
                }
                health.Health = m_health;
                health.CauseOfDeath = m_cause;
            }
        });
    }
}

