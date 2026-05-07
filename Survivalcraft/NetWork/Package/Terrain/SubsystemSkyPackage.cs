using Engine;

namespace Game.NetWork;

public class SubsystemSkyPackage : IPackage {
    public override byte ID => (byte)PackageType.Sky;
    public override ClientStage MinNeedState => ClientStage.Playing;
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

    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref LightningStrikePosition);
        archive.Value(ref isRequest);
        archive.Value(ref m_direction);
    }

    public override void Handle(bool isServer) {
        if (!isServer || (From?.PlayerData != null && NetworkManager.GetEffectiveGameMode(From.PlayerData) == GameMode.Creative)) {
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
            Log.Information($"{From.PlayerData.Name} 鎵撶畻鍦ㄩ潪鍒涢€犳ā寮忎笅浣跨敤闂數");
        }
    }
}