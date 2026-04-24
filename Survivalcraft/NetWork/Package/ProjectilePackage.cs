using Engine;
namespace Game.NetWork.Packages;

public class ProjectilePackage : IPackage {
    public byte ID => (byte)PackageType.Projectile;
    public Client To { get; set; }
    public Client Except { get; set; }
    public Client From { get; set; }
    public ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public enum ProjectileTailInfo : byte {
        None = 0,
        Smoke = 1,
        Fireworks = 2,
        IsOffsetZero = 4
    }

    public int m_value;
    public Vector3 m_position;
    public Vector3 m_velocity;
    public Vector3 m_angularVelocity;
    public Vector3 m_trailOfffset;
    public int m_ownerId;
    public bool m_isFireProjectile;

    public ProjectilePackage() {
    }
    public ProjectilePackage(Projectile projectile) {
        m_value = projectile.Value;
        m_position = projectile.Position;
        m_velocity = projectile.Velocity;
        m_trailOfffset = projectile.TrailOffset;
        m_angularVelocity = projectile.AngularVelocity;
        m_ownerId = projectile.Owner == null ? 0 : projectile.Owner.Entity.Id;
    }
    public void WriteData(PackageStreamWriter writer) {
        writer.Write(m_value);
        writer.Write(m_position);
        writer.Write(m_velocity);
        writer.Write(m_trailOfffset);
        writer.Write(m_angularVelocity);
        writer.Write(m_ownerId);
    }
    public void ReadData(PackageStreamReader reader) {
        m_value = reader.ReadInt32();
        m_position = reader.ReadVector3();
        m_velocity = reader.ReadVector3();
        m_trailOfffset = reader.ReadVector3();
        m_angularVelocity = reader.ReadVector3();
        m_ownerId = reader.ReadInt32();
    }

    public void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        var subsystem = GameManager.Project.FindSubsystem<SubsystemProjectiles>();
        ComponentCreature creature = null;
        if (m_ownerId != 0) {
            GameManager.Project.FindEntity(m_ownerId, (entity) => {
                creature = entity.FindComponent<ComponentCreature>();
            });
        }
        var proj = m_isFireProjectile ? subsystem.FireProjectileNet(m_value, m_position, m_velocity, m_angularVelocity, creature) : subsystem.AddProjectileNet(m_value, m_position, m_velocity, m_angularVelocity, creature);
    }
}
