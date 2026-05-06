using Engine;

namespace Game.NetWork.Packages;

public class HealthSyncPackage : IPackage {
    public override byte ID => (byte)PackageType.HealthSync;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public int m_targetId;
    public float m_health;
    public string m_cause = "";

    public HealthSyncPackage() {
    }

    public HealthSyncPackage(ComponentHealth health) {
        m_targetId = health.Entity.Id;
        m_health = health.Health;
        m_cause = health.CauseOfDeath ?? "";
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_targetId);
        archive.Value(ref m_health);
        archive.Value(ref m_cause);
    }

    public override void Handle(bool isServer) {
        if (isServer) return;
        GameManager.Project.FindEntity(m_targetId, e => {
            ComponentHealth health = e.FindComponent<ComponentHealth>();
            if (health != null) {
                health.Health = m_health;
                health.CauseOfDeath = m_cause;
            }
        });
    }
}

public class DamageSyncPackage : IPackage {
    public override byte ID => (byte)PackageType.DamageSync;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public int m_targetId;
    public float m_health;

    public DamageSyncPackage() {
    }

    public DamageSyncPackage(ComponentDamage health) {
        m_targetId = health.Entity.Id;
        m_health = health.Hitpoints;
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_targetId);
        archive.Value(ref m_health);
    }

    public override void Handle(bool isServer) {
        if (isServer) return;
        GameManager.Project.FindEntity(m_targetId, e => {
            ComponentDamage h = e.FindComponent<ComponentDamage>();
            if (h != null) h.Hitpoints = m_health;
        });
    }
}

public class InjureEventPackage : IPackage {
    public override byte ID => (byte)PackageType.InjureEvent;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public int m_targetId;
    public int m_attackerId;
    public float m_amount;
    public float m_health;
    public string m_cause = "";
    public bool m_ignoreInvulnerability;

    public InjureEventPackage() {
    }

    public InjureEventPackage(ComponentHealth target, ComponentCreature attacker, float amount, string cause, bool ignoreInvulnerability) {
        m_targetId = target.Entity.Id;
        m_attackerId = attacker?.Entity.Id ?? 0;
        m_amount = amount;
        m_health = target.Health;
        m_cause = cause;
        m_ignoreInvulnerability = ignoreInvulnerability;
    }

    public override void Serialize(PackageSerializer archive) {
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

public class InjureRequestPackage : IPackage {
    public enum RequestInjureType {
        Unkonw,
        Choke,
        Suicide,
        Cactus,
        Fall,
        Fire
    }
    public override byte ID => (byte)PackageType.InjureRequest;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public int m_targetId;
    public int m_attackerId;
    public float m_amount;
    public float m_health;
    public bool m_ignoreInvulnerability;
    public string m_cause = "";
    public RequestInjureType m_requestInjureType;

    public InjureRequestPackage() {
    }

    public InjureRequestPackage(ComponentHealth target, ComponentCreature attacker, float amount, string cause, bool ignoreInvulnerability, RequestInjureType requestType) {
        m_targetId = target.Entity.Id;
        m_attackerId = attacker?.Entity.Id ?? 0;
        m_amount = amount;
        m_health = target.Health;
        m_cause = cause;
        m_ignoreInvulnerability = ignoreInvulnerability;
        m_requestInjureType = requestType;
    }

    public override void Serialize(PackageSerializer archive) {
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

public class HitValuePackage : IPackage {
    public override byte ID => (byte)PackageType.HitResult;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public Vector3 m_position;
    public Vector3 m_velocity;
    public Color m_color;
    public string m_text;

    public HitValuePackage() {
    }

    public HitValuePackage(Vector3 position, Vector3 velocity, Color color, string text) {
        m_position = position;
        m_velocity = velocity;
        m_color = color;
        m_text = text;
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_position);
        archive.Value(ref m_velocity);
        archive.Value(ref m_color);
        archive.Value(ref m_text);
    }

    public override void Handle(bool isServer) {
        if (isServer) return;
        HitValueParticleSystem particleSystem = new HitValueParticleSystem(m_position, m_velocity, m_color, m_text);
        float pitch = new Random().Float(-0.2f, 0.2f);
        GameManager.Project.FindSubsystem<SubsystemParticles>(true).AddParticleSystem(particleSystem);
        GameManager.Project.FindSubsystem<SubsystemAudio>().PlaySound("Audio/Swoosh", 1f, pitch, m_position, 3f, false);
    }
}
