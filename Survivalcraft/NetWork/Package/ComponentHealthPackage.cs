using Engine;

namespace Game.NetWork.Packages;

public class ComponentHealthPackage : IPackage
{
    public byte ID => (byte)PackageType.ComponentHealth;
    public Client To { get; set; }
    public Client Except { get; set; }
    public Client From { get; set; }
    public ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public float m_amount;
    public ushort m_attackerId;
    public string m_cuase;
    public ushort m_targetId;
    public bool m_ignoreInvulnerability;

    public Vector3 m_position;
    public Vector3 m_velocity;
    public Color m_color;
    public string m_text;

    public float m_health;

    public enum EventType
    {
        HitResult,
        Injure,
        RequestInjure,
        SyncHealth,
        Damage //鑸硅鐮村潖
    }

    public enum RequestInjureType
    {
        Unkonw,     // 鏈煡
        Choke,      // 鑷潃
        Cactus,     // 浠欎汉鎺屽埡浼?
        Fall,       // 鎽斾激
        Fire        // 鐫€鐏?
    }

    public EventType m_type;
    public RequestInjureType m_requestInjureType;

    public ComponentHealthPackage()
    {
    }

    public ComponentHealthPackage(ComponentHealth health)
    {
        m_targetId = health.Entity.EntityId;
        m_health = health.Health;
        m_type = EventType.SyncHealth;
    }

    public ComponentHealthPackage(ComponentDamage health)
    {
        m_targetId = health.Entity.EntityId;
        m_health = health.Hitpoints;
        m_type = EventType.Damage;
    }

    public ComponentHealthPackage(ComponentHealth target, ComponentCreature attacker, float amount, string cuase, bool ignoreInvulnerability = false, bool isRequest = false, RequestInjureType requestInjureType = RequestInjureType.Unkonw)
    {
        m_type = isRequest ? EventType.RequestInjure : EventType.Injure;
        m_amount = amount;
        m_attackerId = attacker == null ? (ushort)0 : attacker.Entity.EntityId;
        m_health = target.Health;
        m_targetId = target.Entity.EntityId;
        m_cuase = cuase;
        m_ignoreInvulnerability = ignoreInvulnerability;
        this.m_requestInjureType = requestInjureType;
    }

    public ComponentHealthPackage(Vector3 position, Vector3 velocity, Color color, string text)
    {
        m_position = position;
        m_velocity = velocity;
        m_color = color;
        m_text = text;
        m_type = EventType.HitResult;
    }

    public void WriteData(PackageStreamWriter writer)
    {
        writer.WriteEnum(m_type);
        switch (m_type)
        {
            case EventType.RequestInjure:
                writer.Write(m_targetId);
                writer.Write(m_attackerId);
                writer.Write(m_health);
                writer.Write(m_ignoreInvulnerability);
                writer.Write(m_amount);
                writer.Write(m_cuase);
                writer.WriteEnum(m_requestInjureType);
                break;
            case EventType.Injure:
                writer.Write(m_targetId);
                writer.Write(m_attackerId);
                writer.Write(m_health);
                writer.Write(m_ignoreInvulnerability);
                writer.Write(m_amount);
                writer.Write(m_cuase);
                break;
            case EventType.HitResult:
                writer.Write(m_position);
                writer.Write(m_velocity);
                writer.Write(m_color);
                writer.Write(m_text);
                break;
            case EventType.SyncHealth:
                writer.Write(m_targetId);
                writer.Write(m_health);
                break;
            case EventType.Damage:
                writer.Write(m_targetId);
                writer.Write(m_health);
                break;
        }
    }
    public void ReadData(PackageStreamReader reader)
    {
        m_type = reader.ReadEnum<EventType>();
        switch (m_type)
        {
            case EventType.RequestInjure:
                m_targetId = reader.ReadUInt16();
                m_attackerId = reader.ReadUInt16();
                m_health = reader.ReadSingle();
                m_ignoreInvulnerability = reader.ReadBoolean();
                m_amount = reader.ReadSingle();
                m_cuase = reader.ReadString();
                m_requestInjureType = reader.ReadEnum<RequestInjureType>();
                break;
            case EventType.Injure:
                m_targetId = reader.ReadUInt16();
                m_attackerId = reader.ReadUInt16();
                m_health = reader.ReadSingle();
                m_ignoreInvulnerability = reader.ReadBoolean();
                m_amount = reader.ReadSingle();
                m_cuase = reader.ReadString();
                break;
            case EventType.HitResult:
                m_position = reader.ReadVector3();
                m_velocity = reader.ReadVector3();
                m_color = reader.ReadColor();
                m_text = reader.ReadString();
                break;
            case EventType.SyncHealth:
                m_targetId = reader.ReadUInt16();
                m_health = reader.ReadSingle();
                break;
            case EventType.Damage:
                m_targetId = reader.ReadUInt16();
                m_health = reader.ReadSingle();
                break;
        }
    }

    public void Handle(ProjectNet projectNet, bool isServer)
    {
        // 鍓嶇疆杩囨护锛氭湇鍔″櫒鍙鐞?RequestInjure锛屽鎴风鍙鐞嗗叾浠栦簨浠?
        if (NetworkManager.WorkType == WorkType.Server && m_type != EventType.RequestInjure)
        {
            return; // 鏈嶅姟鍣ㄥ拷鐣ラ潪璇锋眰浜嬩欢
        }

        switch (m_type)
        {
            case EventType.RequestInjure:
                projectNet.FindEntityById(m_targetId, (entity) =>
                {
                    // 浠呮帴鍙楀鎴风鍙戣捣鐨勮嚜鏉€浠ュ強鎽旇惤璇锋眰锛屽叾瀹冪幆澧冧激瀹充氦鐢辨湇鍔″櫒鏉冨▉閫昏緫缁撶畻
                    if (m_requestInjureType != RequestInjureType.Choke && m_requestInjureType != RequestInjureType.Fall)
                    {
                        return;
                    }
                    var selfHealth = From.PlayerData?.ComponentPlayer?.ComponentHealth;
                    if (m_attackerId == 0)
                    {
                        selfHealth?.Injure(m_amount, null, m_ignoreInvulnerability, NormalizeCause(m_requestInjureType));
                    }
                });
                break;

            case EventType.Injure:
                projectNet.FindEntityById(m_targetId, (entity) =>
                {
                    var health = entity.FindComponent<ComponentHealth>();
                    if (health == null) return;

                    if (m_attackerId == 0)
                    {
                        health.NetInjure(m_amount, null, m_cuase);
                        health.Health = m_health;
                    }
                    else
                    {
                        projectNet.FindEntityById(m_attackerId, (entity2) =>
                        {
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
                projectNet.FindSubsystem<SubsystemParticles>(throwOnError: true).AddParticleSystem(particleSystem);
                projectNet.FindSubsystem<SubsystemAudio>().PlaySound("Audio/Swoosh", 1f, pitch, m_position, 3f, false);
                break;

            case EventType.SyncHealth:
                projectNet.FindEntityById(m_targetId, (e) =>
                {
                    var h = e.FindComponent<ComponentHealth>();
                    if (h != null) h.Health = m_health;
                });
                break;

            case EventType.Damage:
                projectNet.FindEntityById(m_targetId, (e) =>
                {
                    var h = e.FindComponent<ComponentDamage>();
                    if (h != null) h.Hitpoints = m_health;
                });
                break;
        }
    }

    private static string NormalizeCause(ComponentHealthPackage.RequestInjureType type)
    {
        switch (type)
        {
            case RequestInjureType.Cactus: return "被仙人掌刺入";
            case RequestInjureType.Fall: return "从高处坠落";
            case RequestInjureType.Fire: return "被火焰灼烧";
            case RequestInjureType.Choke: return "窒息";
            default: return "未知";
        }
    }
}

