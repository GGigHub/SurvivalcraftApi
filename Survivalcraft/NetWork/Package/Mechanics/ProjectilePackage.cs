using Engine;

namespace Game.NetWork;

public class ProjectilePackage : IPackage {
    public override byte ID => (byte)PackageType.Projectile;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;

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

    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_value);
        archive.Value(ref m_position);
        archive.Value(ref m_velocity);
        archive.Value(ref m_trailOfffset);
        archive.Value(ref m_angularVelocity);
        archive.Value(ref m_ownerId);
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        var subsystem = GameManager.Project.FindSubsystem<SubsystemProjectiles>();
        ComponentCreature creature = null;
        if (m_ownerId != 0) {
            GameManager.Project.FindEntity(m_ownerId, entity => {
                creature = entity.FindComponent<ComponentCreature>();
            });
        }
        _ = m_isFireProjectile
            ? subsystem.FireProjectileNet(m_value, m_position, m_velocity, m_angularVelocity, creature)
            : subsystem.AddProjectileNet(m_value, m_position, m_velocity, m_angularVelocity, creature);
    }
}