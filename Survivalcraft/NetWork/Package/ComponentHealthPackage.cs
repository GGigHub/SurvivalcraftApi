using Engine;

namespace Game.NetWork.Packages;

public class ComponentHealthPackage : IPackage {
    public override byte ID => (byte)PackageType.Health;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public float m_amount;
    public int m_attackerId;
    public string m_cuase;
    public int m_targetId;
    public bool m_ignoreInvulnerability;

    public Vector3 m_position;
    public Vector3 m_velocity;
    public Color m_color;
    public string m_text;

    public float m_health;

    public enum EventType {
        HitResult,
        Injure,
        RequestInjure,
        SyncHealth,
        Damage
    }

    public enum RequestInjureType {
        Unkonw,
        Choke,
        Suicide,
        Cactus,
        Fall,
        Fire
    }

    public EventType m_type;
    public RequestInjureType m_requestInjureType;

    public ComponentHealthPackage() {
    }

    public ComponentHealthPackage(ComponentHealth health) {
        m_targetId = health.Entity.Id;
        m_health = health.Health;
        m_type = EventType.SyncHealth;
    }

    public ComponentHealthPackage(ComponentDamage health) {
        m_targetId = health.Entity.Id;
        m_health = health.Hitpoints;
        m_type = EventType.Damage;
    }

    public ComponentHealthPackage(ComponentHealth target, ComponentCreature attacker, float amount, string cuase, bool ignoreInvulnerability = false, bool isRequest = false, RequestInjureType requestInjureType = RequestInjureType.Unkonw) {
        m_type = isRequest ? EventType.RequestInjure : EventType.Injure;
        m_amount = amount;
        m_attackerId = attacker == null ? 0 : attacker.Entity.Id;
        m_health = target.Health;
        m_targetId = target.Entity.Id;
        m_cuase = cuase;
        m_ignoreInvulnerability = ignoreInvulnerability;
        m_requestInjureType = requestInjureType;
    }

    public ComponentHealthPackage(Vector3 position, Vector3 velocity, Color color, string text) {
        m_position = position;
        m_velocity = velocity;
        m_color = color;
        m_text = text;
        m_type = EventType.HitResult;
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Enum(ref m_type);
        switch (m_type) {
            case EventType.RequestInjure:
                archive.Value(ref m_targetId);
                archive.Value(ref m_attackerId);
                archive.Value(ref m_health);
                archive.Value(ref m_ignoreInvulnerability);
                archive.Value(ref m_amount);
                archive.Value(ref m_cuase);
                archive.Enum(ref m_requestInjureType);
                break;
            case EventType.Injure:
                archive.Value(ref m_targetId);
                archive.Value(ref m_attackerId);
                archive.Value(ref m_health);
                archive.Value(ref m_ignoreInvulnerability);
                archive.Value(ref m_amount);
                archive.Value(ref m_cuase);
                break;
            case EventType.HitResult:
                archive.Value(ref m_position);
                archive.Value(ref m_velocity);
                archive.Value(ref m_color);
                archive.Value(ref m_text);
                break;
            case EventType.SyncHealth:
            case EventType.Damage:
                archive.Value(ref m_targetId);
                archive.Value(ref m_health);
                break;
        }
    }

    public override void Handle(bool isServer) {
        if (NetworkManager.WorkType == NetWork.WorkType.Server && m_type != EventType.RequestInjure) {
            return;
        }

        switch (m_type) {
            case EventType.RequestInjure:
                GameManager.Project.FindEntity(m_targetId, entity => {
                    if (m_requestInjureType != RequestInjureType.Choke && m_requestInjureType != RequestInjureType.Fall) {
                        return;
                    }
                    var selfHealth = From.PlayerData?.ComponentPlayer?.ComponentHealth;
                    if (m_attackerId == 0) {
                        selfHealth?.Injure(m_amount, null, m_ignoreInvulnerability, NormalizeCause(m_requestInjureType));
                    }
                });
                break;
            case EventType.Injure:
                GameManager.Project.FindEntity(m_targetId, entity => {
                    var health = entity.FindComponent<ComponentHealth>();
                    if (health == null) {
                        return;
                    }

                    if (m_attackerId == 0) {
                        health.NetInjure(m_amount, null, m_cuase);
                        health.Health = m_health;
                    }
                    else {
                        GameManager.Project.FindEntity(m_attackerId, entity2 => {
                            var attacker = entity2.FindComponent<ComponentCreature>();
                            health.NetInjure(m_amount, attacker, m_cuase);
                            health.Health = m_health;
                        });
                    }
                });
                break;
            case EventType.HitResult:
                HitValueParticleSystem particleSystem = new HitValueParticleSystem(m_position, m_velocity, m_color, m_text);
                float pitch = new Random().Float(-0.2f, 0.2f);
                GameManager.Project.FindSubsystem<SubsystemParticles>(throwOnError: true).AddParticleSystem(particleSystem);
                GameManager.Project.FindSubsystem<SubsystemAudio>().PlaySound("Audio/Swoosh", 1f, pitch, m_position, 3f, false);
                break;
            case EventType.SyncHealth:
                GameManager.Project.FindEntity(m_targetId, e => {
                    var h = e.FindComponent<ComponentHealth>();
                    if (h != null) {
                        h.Health = m_health;
                    }
                });
                break;
            case EventType.Damage:
                GameManager.Project.FindEntity(m_targetId, e => {
                    var h = e.FindComponent<ComponentDamage>();
                    if (h != null) {
                        h.Hitpoints = m_health;
                    }
                });
                break;
        }
    }

    private static string NormalizeCause(RequestInjureType type) {
        switch (type) {
            case RequestInjureType.Cactus:
                return "Cactus";
            case RequestInjureType.Fall:
                return "Fall";
            case RequestInjureType.Fire:
                return "Fire";
            case RequestInjureType.Choke:
                return "Choke";
            default:
                return "Unknown";
        }
    }
}
