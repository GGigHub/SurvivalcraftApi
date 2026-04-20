using Engine;
using System;
using System.Net;
using static Game.NetPlayScreen;

namespace Game.NetWork.Packages;

/// <summary>
/// 基础包模板复制
/// </summary>
public class ServerInfoPackage : IPackage
{
    public byte ID => (byte)PackageType.ServerInfo;
    public Client To { get; set; }
    public Client Except { get; set; }
    public Client From { get; set; }

    public ClientState MinNeedState => ClientState.ProjectLoaded;

    public string m_version;
    ushort m_clientCount;
    ushort m_maxPlayerCount;
    GameMode m_gameMode;
    bool m_needPasswd;
    float m_timeOfDay;
    bool m_requestInfo;
    public int Ping;
    public string m_modServerAddress;

    public Season Season { get; set; } // 添加季节属性
    public float TimeOfSeason { get; set; } // 添加季节进度属性
    public bool IsLegacyVersion { get; set; }

    public ServerInfoPackage() { }


    public ServerInfoPackage(bool requestInfo)
    {
        m_requestInfo = requestInfo;
        if (!m_requestInfo)
        {
            m_version = ProjectNet.Server_Version;
            m_clientCount = (ushort)NetworkManager.Net.ClientCount;
            m_maxPlayerCount = ProjectNet.subsystemGameInfo.WorldSettings.MaxOnlinePlayerCount;
            m_gameMode = ProjectNet.subsystemGameInfo.WorldSettings.GameMode;
            m_needPasswd = (string.IsNullOrEmpty(ProjectNet.subsystemGameInfo.WorldSettings.Password) == false);
            m_timeOfDay = ProjectNet.subsystemTimeOfDay.CalculateTimeOfDay();
            Season = GameManager.Project.FindSubsystem<SubsystemSeasons>()?.Season ?? (Season)(-1); // 获取当前季节
            TimeOfSeason = GameManager.Project.FindSubsystem<SubsystemSeasons>()?.TimeOfSeason ?? -1f; // 获取当前季节进度
        }
    }

    public void Handle(ProjectNet projectNet, bool isServer)
    {
        if (m_requestInfo)
        {
            NetworkManager.Net.SendWriterFromPackage(new ServerInfoPackage(false), From.IPPoint);
        }
        else
        {
            var p = ScreensManager.FindScreen<NetPlayScreen>("NetPlay");
            var c = new Connect();
            c.state = ConnectState.Avaliable;
            c.ip = From.IPPoint.ToString();
            c.name = c.ip;
            c.gameMode = m_gameMode;
            c.HasPassword = m_needPasswd;
            c.maxCount = m_maxPlayerCount;
            c.playerCount = m_clientCount;
            c.fromBroadcast = From.IsLocalRemote;
            c.IsLocalRemote = From.IsLocalRemote;
            c.fromLocal = false;
            c.fromCommunity = false;
            c.usedTime = Ping;
            c.version = m_version;
            c.TimeOfDay = m_timeOfDay;
            c.season = Season; // 设置季节
            c.timeOfSeason = TimeOfSeason; // 设置季节进度
            c.isLegacyVersion = IsLegacyVersion;
            if (IpToDNS.TryGetValue(c.ip, out string dns))
            {
                c.ip = dns;
            }
            if (DNSToName.TryGetValue(c.ip, out string name))
            {
                c.name = name;
            }
            if (p.CheckConnectExists(c, out var found))
            {
                found.state = c.state;
                found.ip = string.IsNullOrEmpty(dns) ? found.ip : c.ip;
                found.name = string.IsNullOrEmpty(name) ? found.name : c.name;
                found.gameMode = c.gameMode;
                found.HasPassword = c.HasPassword;
                found.maxCount = c.maxCount;
                found.playerCount = c.playerCount;
                found.fromBroadcast = c.fromBroadcast;
                found.IsLocalRemote = c.IsLocalRemote;
                found.usedTime = c.usedTime;
                found.version = c.version;
                found.TimeOfDay = c.TimeOfDay;
                found.season = Season; // 更新季节
                found.timeOfSeason = TimeOfSeason; // 更新季节进度
                found.isLegacyVersion = IsLegacyVersion;
            }
            else
            {
                if (From.IsLocalRemote) //局域网
                {
                    if (p.CheckSaveConnectExists(c, out Connect f))
                    {
                        ModsManager.SaveConnects.Remove(f);
                    }
                    ModsManager.SaveConnects.Add(c);
                }
            }
            p.UpdateList();
        }
    }

    public void ReadData(PackageStreamReader reader)
    {
        m_requestInfo = reader.ReadBoolean();
        if (!m_requestInfo)
        {
            m_version = reader.ReadString();
            m_clientCount = reader.ReadUInt16();
            m_maxPlayerCount = reader.ReadUInt16();
            m_gameMode = reader.ReadEnum<GameMode>();
            reader.ReadBoolean();
            m_needPasswd = reader.ReadBoolean();
            m_timeOfDay = reader.ReadSingle();
            try
            {
                Season = (Season)reader.ReadInt32(); // 读取季节
                TimeOfSeason = reader.ReadSingle(); // 读取季节进度
            }
            catch
            {
                // 兼容旧版本
                IsLegacyVersion = true;
            }
        }
    }
    public void WriteData(PackageStreamWriter writer)
    {
        writer.Write(m_requestInfo);
        if (!m_requestInfo)
        {
            writer.Write(m_version);
            writer.Write(m_clientCount);
            writer.Write(m_maxPlayerCount);
            writer.WriteEnum(m_gameMode);
            writer.Write(false);
            writer.Write(m_needPasswd);
            writer.Write(m_timeOfDay);
            writer.Write((int)Season); // 写入季节
            writer.Write(TimeOfSeason); // 写入季节进度
        }
    }
}
