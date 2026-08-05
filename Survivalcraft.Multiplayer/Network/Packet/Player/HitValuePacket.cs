using Engine;

namespace Game.Network;

public class HitValuePacket : Packet {
    public override byte ID => (byte)PacketType.HitResult;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;
    public Vector3 m_position;
    public Vector3 m_velocity;
    public Color m_color;
    public string m_text;
    public bool m_velocityIsFinal;
    public int m_attackerPlayerIndex = -1;

    public HitValuePacket() {
    }

    public HitValuePacket(Vector3 position, Vector3 velocity, Color color, string text) {
        m_position = position;
        m_velocity = velocity;
        m_color = color;
        m_text = text;
    }

    public HitValuePacket(Vector3 position, Vector3 velocity, Color color, string text, bool velocityIsFinal) : this(position, velocity, color, text) {
        m_velocityIsFinal = velocityIsFinal;
    }

    public HitValuePacket(Vector3 position, Vector3 velocity, Color color, string text, bool velocityIsFinal, int attackerPlayerIndex) : this(
        position,
        velocity,
        color,
        text,
        velocityIsFinal
    ) {
        m_attackerPlayerIndex = attackerPlayerIndex;
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_position);
        archive.Value(ref m_velocity);
        archive.Value(ref m_color);
        archive.Value(ref m_text);
        archive.TryValue(ref m_velocityIsFinal);
        archive.TryValue(ref m_attackerPlayerIndex);
    }

    public override void Handle(bool isServer) {
        if (isServer) return;
        HitValueParticleSystem particleSystem = new HitValueParticleSystem(m_position, m_velocity, m_color, m_text, !m_velocityIsFinal);
        float pitch = new Random().Float(-0.2f, 0.2f);
        GameManager.Project.FindSubsystem<SubsystemParticles>(true).AddParticleSystem(particleSystem);
        if (!IsOwnServerEcho()) {
            GameManager.Project.FindSubsystem<SubsystemAudio>().PlaySound("Audio/Swoosh", 1f, pitch, m_position, 3f, false);
        }
    }

    private bool IsOwnServerEcho() {
        if (m_attackerPlayerIndex < 0 || GameManager.Project == null) {
            return false;
        }
        SubsystemPlayers subsystemPlayers = GameManager.Project.FindSubsystem<SubsystemPlayers>();
        if (subsystemPlayers == null) {
            return false;
        }
        foreach (PlayerData playerData in subsystemPlayers.PlayersData) {
            if (playerData.IsMainPlayer && playerData.PlayerIndex == m_attackerPlayerIndex) {
                return true;
            }
        }
        return false;
    }

}