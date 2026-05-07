using System.IO.Compression;
using System.Net;
using System.Net.NetworkInformation;
using System.Xml.Linq;
using Engine;
using Engine.Graphics;
using Game.Network;
using GameEntitySystem;
using LiteNetLib;
using TemplatesDatabase;

namespace Game.Network;

public enum ClientStage {
    NotConnected,
    Connected,
    ProjectLoaded,
    LoadTerrain,
    Playing
}

public enum WorkType {
    Local = 0,
    Server = 1,
    Client = 2
}

public enum NetworkStage {
    NotConnected,
    WaitForClientList,
    Connected
}

public static class NetworkManager {
    public const int DISCONNECT_TIMEOUT = 10000;
    public static NetworkServer Server { get; private set; }
    public static NetworkClient Client { get; private set; }

    public static Texture2D BlockTexture;

    public static bool IsOfflineMode;
    public static WorkType WorkType => Server != null ? Server.WorkType : Client != null ? Client.WorkType : Game.Network.WorkType.Local;
    public static Client Self => Server != null ? Server.Self : Client != null ? Client.Self : null;
    public static Dictionary<int, Client> Clients => Server != null ? Server.Clients : Client != null ? Client.Clients : [];
    public static IEnumerable<Client> Peers => Server != null ? Server.Peers : Client != null ? Client.Peers : [];
    public static int ClientCount => Server != null ? Server.ClientCount : Client != null ? Client.ClientCount : 0;
    public static NetworkStage CurrentStage => Server != null ? Server.CurrentStage : Client != null ? Client.CurrentStage : NetworkStage.NotConnected;
    public static bool IsNetRunning => (Server != null ? Server.NetManager : Client != null ? Client.NetManager : null)?.IsRunning ?? false;
    public static Action<Client> OnClientStateChanged {
        get => Server != null ? Server.OnClientStateChanged : Client != null ? Client.OnClientStateChanged : null;
        set {
            if (Server != null) {
                Server.OnClientStateChanged = value;
            }
            else if (Client != null) {
                Client.OnClientStateChanged = value;
            }
        }
    }

    /// <summary>
    /// 获取玩家的有效模�?
    /// </summary>
    public static GameMode GetEffectiveGameMode(PlayerData pd) {
        return GameManager.Project.FindSubsystem<SubsystemGameInfo>()?.WorldSettings?.GameMode ?? GameMode.Survival;
    }

    public static ComponentPlayer MainPlayer {
        get {
            if (GameManager.Project != null && Self != null) {
                return GameManager.Project.FindSubsystem<SubsystemPlayers>().ComponentPlayers.FirstOrDefault(p => p.PlayerData.PlayerGUID == Self.PlayerGuid);
            }
            return null;
        }
    }

    public static bool StartServer() {
        Server = new NetworkServer();
        return Server.StartServer(SettingsManager.ServerPort);
    }

    public static void ConnectServer(IPEndPoint ep, string passwd = "", string? onlineAccessToken = null) {
        Client = new NetworkClient();
        Client.ConnectServer(ep, passwd, onlineAccessToken);
    }

    public static void QueuePacket(Packet packet) {
        if (Server != null) {
            lock (Server.PendingPackets) {
                Server.PendingPackets.Add(packet);
            }
            return;
        }
        if (Client != null) {
            lock (Client.PendingPackets) {
                Client.PendingPackets.Add(packet);
            }
        }
    }

    public static byte[] GetNowProject(Project project) {
        ValuesDictionary rootNode = CreateProjectRootNode(project);
        return rootNode.ToMessagePack();
    }

    private static ValuesDictionary CreateProjectRootNode(Project project) {
        var projData = Save(project);
        ValuesDictionary rootNode = [];
        projData.Save(rootNode);
        return rootNode;
    }

    private static ProjectData Save(Project project) {
        ProjectData projectData = new() {
            ValuesDictionary = new ValuesDictionary {
                DatabaseObject = project.m_projectTemplate
            }
        };
        foreach (Subsystem subsystem in GameManager.Project.m_subsystems) {
            ValuesDictionary valuesDictionary = new ValuesDictionary();
            subsystem.Save(valuesDictionary);
            if (valuesDictionary.Count > 0) {
                projectData.ValuesDictionary.SetValue(subsystem.ValuesDictionary.DatabaseObject.Name, valuesDictionary);
            }
        }
        return projectData;
    }

    public static byte[] Compress(Stream stream) {
        stream.Position = 0L;
        byte[] data = new byte[stream.Length];
        stream.ReadExactly(data, 0, (int)stream.Length);
        return Compress(data);
    }

    public static byte[] Compress(byte[] datas) {
        byte[] data = null;
        using (var outStream = new MemoryStream()) {
            using (var zipStream = new DeflateStream(outStream, CompressionMode.Compress)) {
                zipStream.Write(datas, 0, datas.Length);
            }
            data = outStream.ToArray();
        }
        return data;
    }

    public static byte[] Decompress(byte[] inputBytes) {
        var outStream = new MemoryStream();
        using (var inputStream = new MemoryStream(inputBytes)) {
            using (var zipStream = new DeflateStream(inputStream, CompressionMode.Decompress)) {
                zipStream.CopyTo(outStream);
                zipStream.Close();
            }
        }
        return outStream.ToArray();
    }

    public static Client GetClientByID(byte id) {
        if (Server != null) return Server.GetClientByID(id);
        return Client != null ? Client.GetClientByID(id) : null;
    }

    public static Client GetClientByGUID(Guid? guid) {
        if (Server != null) return Server.GetClientByGUID(guid);
        return Client != null ? Client.GetClientByGUID(guid) : null;
    }

    public static void AddClient(Client client) {
        if (Server != null) {
            Server.AddClient(client);
        }
        else {
            Client.AddClient(client);
        }
    }

    public static Client CreateClient(NetPeer peer, Guid tokenId, Guid guid, string dataId, string nickname) =>
        Server.CreateClient(peer, tokenId, guid, dataId, nickname);

    public static void Stop() {
        if (Server != null) {
            Server.Stop();
        }
        else {
            Client?.Stop();
        }
    }

    public static void Update() {
        Client?.Update();
        Server?.Update();
    }
}


