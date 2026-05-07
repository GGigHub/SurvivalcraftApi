using Engine;

namespace Game.NetWork;

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