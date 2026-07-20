using Engine;

namespace Game.Network;

public class SubsystemSkyPacket : Packet {
    public override byte ID => (byte)PacketType.Sky;
    public override NetworkState MinNeedState => NetworkState.Playing;

    public Vector3 LightningStrikePosition;
    public Vector3 m_direction;

    public SubsystemSkyPacket() {
    }

    public SubsystemSkyPacket(Vector3 position) {
        LightningStrikePosition = position;
    }

    public SubsystemSkyPacket(Vector3 position, Vector3 direction) {
        LightningStrikePosition = position;
        m_direction = direction;
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref LightningStrikePosition);
        archive.Value(ref m_direction);
    }

    public override void Handle(bool isServer) {
        if (m_direction != Vector3.Zero) {
            GameManager.Project.FindSubsystem<SubsystemWeather>().ManualLightingStrike(LightningStrikePosition, m_direction);
        }
        else if (!isServer) {
            GameManager.Project.FindSubsystem<SubsystemSky>().MakeLightningStrike(LightningStrikePosition, true);
        }
    }
}

