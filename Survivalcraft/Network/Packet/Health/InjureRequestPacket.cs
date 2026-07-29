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
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;
    public int m_targetId;
    public int m_attackerId;
    public float m_amount;
    public float m_health;
    public bool m_ignoreInvulnerability;
    public string m_cause = "";
    public RequestInjureType m_requestInjureType;
    public long m_requestId;

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
        m_requestId = NetworkMutationRequestTracker.CreateRequestId();
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_targetId);
        archive.Value(ref m_attackerId);
        archive.Value(ref m_health);
        archive.Value(ref m_ignoreInvulnerability);
        archive.Value(ref m_amount);
        archive.Value(ref m_cause);
        archive.Enum(ref m_requestInjureType);
        archive.Value(ref m_requestId);
    }

    public override void Handle(bool isServer) {
        if (!isServer) return;
        if (m_requestInjureType != RequestInjureType.Choke) {
            return;
        }
        var selfHealth = From?.GetPlayerData()?.ComponentPlayer?.ComponentHealth;
        if (selfHealth == null
            || m_targetId != selfHealth.Entity.Id
            || m_attackerId != 0
            || !float.IsFinite(m_amount)
            || MathF.Abs(m_amount - 0.1f) > 0.0001f) {
            return;
        }
        if (!NetworkMutationRequestTracker.TryAccept(
                From,
                NetworkMutationDomain.PlayerInjury,
                m_requestId)) {
            return;
        }
        selfHealth.Injure(0.1f, null, true, NormalizeCause(m_requestInjureType));
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

    public static void SendFallDamage(ComponentHealth health, float fallDamage, string cause) {
        ComponentPlayer riderPlayer = health.m_componentCreature.ComponentBody.ChildBodies.Count > 0
        ? health.m_componentCreature.ComponentBody.ChildBodies[0].Entity?.FindComponent<ComponentPlayer>()
        : null;

        bool flagMainRider = riderPlayer != null && riderPlayer.PlayerData.IsMainPlayer;
        bool flagMainPlayer = health.m_componentPlayer != null && health.m_componentPlayer.PlayerData.IsMainPlayer;
        bool flagNoRider = health.m_componentCreature.ComponentBody.ChildBodies.Count == 0;

        if (NetworkManager.IsServerRunning) {
            if (flagMainPlayer || flagMainRider || flagNoRider) {
                health.Injure(fallDamage, null, false, cause);
            }
        }
    }
}

