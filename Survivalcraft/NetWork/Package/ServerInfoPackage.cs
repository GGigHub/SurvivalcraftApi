using static Game.NetPlayScreen;

namespace Game.NetWork.Packages;

/// <summary>
/// 鍩虹鍖呮ā鏉垮鍒?
/// </summary>
public class ServerInfoPackage : IPackage {
    public override byte ID => (byte)PackageType.ServerInfo;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;

    public string m_version;
    ushort m_clientCount;
    ushort m_maxPlayerCount;
    GameMode m_gameMode;
    bool m_needPasswd;
    float m_timeOfDay;
    bool m_requestInfo;
    public int Ping;
    public string m_modServerAddress;

    public Season Season { get; set; }
    public float TimeOfSeason { get; set; }
    public bool IsLegacyVersion { get; set; }

    public ServerInfoPackage() {
    }

    public ServerInfoPackage(bool requestInfo) {
        m_requestInfo = requestInfo;
        if (!m_requestInfo) {
            m_clientCount = (ushort)NetworkManager.ClientCount;
            m_maxPlayerCount = GameManager.Project.FindSubsystem<SubsystemGameInfo>().WorldSettings.MaxOnlinePlayerCount;
            m_gameMode = GameManager.Project.FindSubsystem<SubsystemGameInfo>().WorldSettings.GameMode;
            m_needPasswd = string.IsNullOrEmpty(GameManager.Project.FindSubsystem<SubsystemGameInfo>().WorldSettings.Password) == false;
            m_timeOfDay = GameManager.Project.FindSubsystem<SubsystemTimeOfDay>().CalculateTimeOfDay();
            Season = GameManager.Project.FindSubsystem<SubsystemSeasons>()?.Season ?? (Season)(-1);
            TimeOfSeason = GameManager.Project.FindSubsystem<SubsystemSeasons>()?.TimeOfSeason ?? -1f;
        }
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_requestInfo);
        if (!m_requestInfo) {
            archive.Value(ref m_version);
            archive.Value(ref m_clientCount);
            archive.Value(ref m_maxPlayerCount);
            archive.Enum(ref m_gameMode);
            bool reserved = false;
            archive.Value(ref reserved);
            archive.Value(ref m_needPasswd);
            archive.Value(ref m_timeOfDay);
            int season = (int)Season;
            archive.Value(ref season);
            float timeOfSeason = TimeOfSeason;
            archive.Value(ref timeOfSeason);
            if (archive.IsReading) {
                try {
                    Season = (Season)season;
                    TimeOfSeason = timeOfSeason;
                }
                catch {
                    IsLegacyVersion = true;
                }
            }
        }
    }

    public override void Handle(bool isServer) {
        if (m_requestInfo) {
            NetworkManager.Server.SendWriterFromPackage(new ServerInfoPackage(false), From.IPPoint);
        }
        else {
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
            c.season = Season;
            c.timeOfSeason = TimeOfSeason;
            c.isLegacyVersion = IsLegacyVersion;
            if (IpToDNS.TryGetValue(c.ip, out string dns)) {
                c.ip = dns;
            }
            if (DNSToName.TryGetValue(c.ip, out string name)) {
                c.name = name;
            }
            if (p.CheckConnectExists(c, out var found)) {
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
                found.season = Season;
                found.timeOfSeason = TimeOfSeason;
                found.isLegacyVersion = IsLegacyVersion;
            }
            else if (From.IsLocalRemote) {
                if (p.CheckSaveConnectExists(c, out Connect f)) {
                    ModsManager.SaveConnects.Remove(f);
                }
                ModsManager.SaveConnects.Add(c);
            }
            p.UpdateList();
        }
    }
}
