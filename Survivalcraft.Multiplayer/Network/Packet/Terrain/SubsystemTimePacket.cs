using Engine;

namespace Game.Network;

public class SubsystemTimePacket : Packet {
    public override byte ID => (byte)PacketType.Time;
    public override NetworkState MinNeedState => NetworkState.Playing;
    public double m_time;
    public double m_timeOfDayOffset;
    public float m_timeOfYear;

    public SubsystemTimePacket() {
    }

    public SubsystemTimePacket(double TotalElapsedGameTime, double TimeOfDayOffset) {
        m_time = TotalElapsedGameTime;
        m_timeOfDayOffset = TimeOfDayOffset;
        m_timeOfYear = GameManager.Project.FindSubsystem<SubsystemGameInfo>()?.WorldSettings?.TimeOfYear ?? 0f;
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_time);
        archive.Value(ref m_timeOfDayOffset);
        archive.Value(ref m_timeOfYear);
    }

    public override void Handle(bool isServer) {
        var m_info = GameManager.Project.FindSubsystem<SubsystemGameInfo>();
        bool canApplyTime = !isServer
            || (From?.GetPlayerData() != null && GameManager.Project.FindSubsystem<SubsystemGameInfo>()?.WorldSettings?.GameMode == GameMode.Creative);
        if (canApplyTime) {
            m_info.TotalElapsedGameTime = m_time;
            m_info.TimeOfDay.TimeOfDayOffset = m_timeOfDayOffset;
            SubsystemTime subsystemTime = GameManager.Project.FindSubsystem<SubsystemTime>();
            if (subsystemTime != null) {
                subsystemTime.m_gameTime = m_time;
            }
        }
        else {
            if (From?.GetPlayerData() != null) Log.Information($"{From.GetPlayerData().Name} 打算在非创造模式下修改时间");
        }
        if (!isServer) {
            m_info.WorldSettings.TimeOfYear = m_timeOfYear;
        }
    }
}

