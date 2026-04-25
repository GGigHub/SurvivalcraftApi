using Engine;

namespace Game.NetWork.Packages;

public class SubsystemSkyPackage : IPackage {
    public byte ID => (byte)PackageType.Sky;
    public Client To { get; set; }
    public Client Except { get; set; }
    public Client From { get; set; }
    public ClientStage MinNeedState => ClientStage.Playing;
    public Vector3 LightningStrikePosition;
    public Vector3 m_direction;
    public bool isRequest;

    public SubsystemSkyPackage() {

    }
    public SubsystemSkyPackage(Vector3 position) {
        LightningStrikePosition = position;
    }
    public SubsystemSkyPackage(Vector3 position, Vector3 direction) {
        LightningStrikePosition = position;
        m_direction = direction;
        isRequest = true;
    }

    public void WriteData(PackageStreamWriter writer) {
        writer.Write(LightningStrikePosition);
        writer.Write(isRequest);
        writer.Write(m_direction);
    }
    public void ReadData(PackageStreamReader reader) {
        LightningStrikePosition = reader.ReadVector3();
        isRequest = reader.ReadBoolean();
        m_direction = reader.ReadVector3();
    }

    public void Handle(bool isServer) {
        if (!isServer || (From?.PlayerData != null && NetworkManager.GetEffectiveGameMode(From.PlayerData) == GameMode.Creative)) {
            SubsystemSky sky = GameManager.Project.FindSubsystem<SubsystemSky>();
            if (isRequest) {
                GameManager.Project.FindSubsystem<SubsystemWeather>().ManualLightingStrike(LightningStrikePosition, m_direction);
                sky.MakeLightningStrike(LightningStrikePosition, true);
            }
            else sky.NetMakeLightningStrike(LightningStrikePosition, false);
        }
        else {
            Log.Information($"{From.PlayerData.Name} 打算在非创造模式下使用闪电");
        }

    }
}
