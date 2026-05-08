using Engine;

namespace Game.Network;

public class SubsystemSkyPacket : Packet {
    public override byte ID => (byte)PacketType.Sky;
    public override ClientStage MinNeedState => ClientStage.Playing;
    public Vector3 LightningStrikePosition;
    public Vector3 m_direction;
    public bool isRequest;

    public SubsystemSkyPacket() {
    }

    public SubsystemSkyPacket(Vector3 position) {
        LightningStrikePosition = position;
    }

    public SubsystemSkyPacket(Vector3 position, Vector3 direction) {
        LightningStrikePosition = position;
        m_direction = direction;
        isRequest = true;
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref LightningStrikePosition);
        archive.Value(ref isRequest);
        archive.Value(ref m_direction);
    }

    public override void Handle(bool isServer) {
        if (!isServer || (From?.PlayerData != null && GameManager.Project.FindSubsystem<SubsystemGameInfo>()?.WorldSettings?.GameMode == GameMode.Creative)) {
            SubsystemSky sky = GameManager.Project.FindSubsystem<SubsystemSky>();
            if (isRequest) {
                GameManager.Project.FindSubsystem<SubsystemWeather>().ManualLightingStrike(LightningStrikePosition, m_direction);
                sky.MakeLightningStrike(LightningStrikePosition, true);
            }
            else {
                sky.NetMakeLightningStrike(LightningStrikePosition, false);
            }
        }
        else {
            Log.Information($"{From.PlayerData.Name} 打算在非创造模式下使用闪电");
        }
    }
}

