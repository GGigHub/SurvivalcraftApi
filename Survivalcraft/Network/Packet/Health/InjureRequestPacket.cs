using Engine;

namespace Game.Network;

public class InjureRequestPacket : Packet {
    public enum RequestInjureType {
        Unkonw,
        Choke,
        Suicide,
        Cactus,
        Fall,
        Fire
    }
    public override byte ID => (byte)PacketType.InjureRequest;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public int m_targetId;
    public int m_attackerId;
    public float m_amount;
    public float m_health;
    public bool m_ignoreInvulnerability;
    public string m_cause = "";
    public RequestInjureType m_requestInjureType;

    public InjureRequestPacket() {
    }

    public InjureRequestPacket(ComponentHealth target, ComponentCreature attacker, float amount, string cause, bool ignoreInvulnerability, RequestInjureType requestType) {
        m_targetId = target.Entity.Id;
        m_attackerId = attacker?.Entity.Id ?? 0;
        m_amount = amount;
        m_health = target.Health;
        m_cause = cause;
        m_ignoreInvulnerability = ignoreInvulnerability;
        m_requestInjureType = requestType;
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_targetId);
        archive.Value(ref m_attackerId);
        archive.Value(ref m_health);
        archive.Value(ref m_ignoreInvulnerability);
        archive.Value(ref m_amount);
        archive.Value(ref m_cause);
        archive.Enum(ref m_requestInjureType);
    }

    public override void Handle(bool isServer) {
        if (!isServer) return;
        GameManager.Project.FindEntity(m_targetId, entity => {
            if (m_requestInjureType != RequestInjureType.Choke && m_requestInjureType != RequestInjureType.Fall) {
                return;
            }
            var selfHealth = From.PlayerData?.ComponentPlayer?.ComponentHealth;
            if (m_attackerId == 0) {
                selfHealth?.Injure(m_amount, null, m_ignoreInvulnerability, NormalizeCause(m_requestInjureType));
            }
        });
    }

    private static string NormalizeCause(RequestInjureType type) {
        switch (type) {
            case RequestInjureType.Cactus: return "Cactus";
            case RequestInjureType.Fall: return "Fall";
            case RequestInjureType.Fire: return "Fire";
            case RequestInjureType.Choke: return "Choke";
            default: return "Unknown";
        }
    }
}

