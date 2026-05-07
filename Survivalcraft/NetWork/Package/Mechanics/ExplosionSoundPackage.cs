using Engine;

namespace Game.NetWork;

public class ExplosionSoundPackage : IPackage {
    public override byte ID => (byte)PackageType.ExplosionSound;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;

    public Vector3 m_position;
    public float m_level;
    public float m_delay;

    public ExplosionSoundPackage() {
    }

    public ExplosionSoundPackage(Vector3 position, float level, float delay) {
        m_position = position;
        m_level = level;
        m_delay = delay;
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_position);
        archive.Value(ref m_level);
        archive.Value(ref m_delay);
    }

    public override void Handle(bool isServer) {
        if (isServer) return;
        var sub = GameManager.Project.FindSubsystem<SubsystemExplosions>();
        sub?.PlayExplosionSound(m_position, m_level, m_delay, true);
    }
}