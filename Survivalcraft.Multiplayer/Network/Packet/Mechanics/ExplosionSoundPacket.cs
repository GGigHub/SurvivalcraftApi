using Engine;
using GameEntitySystem;

namespace Game.Network;

public class ExplosionSoundPacket : Packet {
    public override byte ID => (byte)PacketType.ExplosionSound;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;

    public long m_eventId;
    public Vector3 m_position;
    public float m_level;
    public float m_delay;

    public ExplosionSoundPacket() { }

    public ExplosionSoundPacket(long eventId, Vector3 position, float level, float delay) {
        m_eventId = eventId;
        m_position = position;
        m_level = level;
        m_delay = delay;
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_eventId);
        archive.Value(ref m_position);
        archive.Value(ref m_level);
        archive.Value(ref m_delay);
    }

    public override void Handle(bool isServer) {
        Project project = GameManager.Project;
        if (isServer
            || project == null
            || !IsValidState()
            || !ExplosionEventTracker.TryAcceptSound(project, m_eventId)) {
            return;
        }
        project.FindSubsystem<SubsystemExplosions>()
            ?.PlayExplosionSound(m_position, m_level, m_delay, true);
    }

    internal bool IsValidState() =>
        m_eventId > 0
        && float.IsFinite(m_position.X)
        && float.IsFinite(m_position.Y)
        && float.IsFinite(m_position.Z)
        && Math.Abs((double)m_position.X) <= 16_777_216
        && m_position.Y is >= -256f and <= 512f
        && Math.Abs((double)m_position.Z) <= 16_777_216
        && float.IsFinite(m_level)
        && m_level is > 0f and <= 100_000_000f
        && float.IsFinite(m_delay)
        && m_delay is >= 0f and <= 120f;
}
