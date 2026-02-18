using Engine;

namespace Game.NetWork.Packages;

public class SubsystemTimePackage : IPackage
{
    public byte ID => (byte)PackageType.SubsystemTime;
    public Client To { get; set; }
    public Client Except { get; set; }
    public Client From { get; set; }
    public ClientState MinNeedState => ClientState.Playing;
    public double m_time;
    public double m_timeOfDayOffset;

    public SubsystemTimePackage()
    {
    }

    public SubsystemTimePackage(double TotalElapsedGameTime, double TimeOfDayOffset)
    {
        m_time = TotalElapsedGameTime;
        m_timeOfDayOffset = TimeOfDayOffset;
    }

    public void WriteData(PackageStreamWriter writer)
    {
        writer.Write(m_time);
        writer.Write(m_timeOfDayOffset);
    }
    public void ReadData(PackageStreamReader reader)
    {
        m_time = reader.ReadDouble();
        m_timeOfDayOffset = reader.ReadDouble();
    }

    public void Handle(ProjectNet projectNet, NetNode netNode, bool isServer)
    {
        var m_info = projectNet.FindSubsystem<SubsystemGameInfo>();
        if (!isServer || (From?.PlayerData != null && CommonLib.GetEffectiveGameMode(From.PlayerData) == GameMode.Creative))
        {
            m_info.TotalElapsedGameTime = m_time;
            m_info.TimeOfDay.TimeOfDayOffset = m_timeOfDayOffset;
        }
        else
        {
            if (From.PlayerData != null) Log.Information($"{From.PlayerData.Name} 打算在非创造模式下修改时间");
        }
    }
}

