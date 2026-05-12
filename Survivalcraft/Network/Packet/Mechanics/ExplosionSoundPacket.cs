using Engine;

namespace Game.Network;

public class ExplosionSoundPacket : Packet {
    public override byte ID => (byte)PacketType.ExplosionSound;
    public override ClientState MinNeedState => ClientState.ProjectLoaded;

    public Vector3 m_position;
    public float m_level;
    public float m_delay;

    public ExplosionSoundPacket() {
    }

    public ExplosionSoundPacket(Vector3 position, float level, float delay) {
        m_position = position;
        m_level = level;
        m_delay = delay;
    }

    public override void Serialize(PacketSerializer archive) {
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

