using Engine;

namespace Game.NetWork;

public class SubsystemTimePackage : IPackage {
    public override byte ID => (byte)PackageType.Time;
    public override ClientStage MinNeedState => ClientStage.Playing;
    public double m_time;
    public double m_timeOfDayOffset;

    public SubsystemTimePackage() {
    }

    public SubsystemTimePackage(double TotalElapsedGameTime, double TimeOfDayOffset) {
        m_time = TotalElapsedGameTime;
        m_timeOfDayOffset = TimeOfDayOffset;
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_time);
        archive.Value(ref m_timeOfDayOffset);
    }

    public override void Handle(bool isServer) {
        var m_info = GameManager.Project.FindSubsystem<SubsystemGameInfo>();
        if (!isServer || (From?.PlayerData != null && NetworkManager.GetEffectiveGameMode(From.PlayerData) == GameMode.Creative)) {
            m_info.TotalElapsedGameTime = m_time;
            m_info.TimeOfDay.TimeOfDayOffset = m_timeOfDayOffset;
        }
        else {
            if (From.PlayerData != null) Log.Information($"{From.PlayerData.Name} 鎵撶畻鍦ㄩ潪鍒涢€犳ā寮忎笅淇敼鏃堕棿");
        }
    }
}