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

public static class NetworkService {
    public static ONetworkServer Server { get; private set; }
    public static ONetworkClient Client { get; private set; }
    public static ClientSession Self => Server?.Self ?? Client?.Self;
    public static Dictionary<int, ClientSession> Clients => Server != null ? Server.Clients : Client != null ? Client.Clients : [];
    public static IEnumerable<ClientSession> Peers => Server != null ? Server.Peers : Client != null ? Client.Clients.Values.Where(c => c != Client.Self) : [];
    public static bool IsServer => Server != null && Server.NetManager.IsRunning;
    public static bool IsClient => Client != null && Client.NetManager.IsRunning;
    public static Guid NetworkGuid => Guid.Parse(SettingsManager.OnlineAccessToken);
    public static NetworkState SelfState => Self.NetworkState;
    public static int SenderPlayerIndex { get; set; } = -1; // Set by packet decoding

    public static ComponentPlayer MainPlayer {
        get {
            if (GameManager.Project != null && Self != null) {
                return GameManager.Project.FindSubsystem<SubsystemPlayers>().ComponentPlayers.FirstOrDefault(p => p.PlayerData.NetworkGuid == Self.NetworkGuid);
            }
            return null;
        }
    }

    public static void Initialize() {
        PacketManager.Initialize();
        RootManager.Initialize();
    }

    public static bool StartServer() {
        Server = new ONetworkServer();
        return Server.StartServer(SettingsManager.ServerPort);
    }

    public static bool ConnectServer(IPEndPoint ep, string passwd = "", string? onlineAccessToken = null) {
        Client = new ONetworkClient();
        return Client.ConnectServer(ep, passwd, onlineAccessToken);
    }

    public static void QueuePacket(Packet packet) {
        Server?.PendingPackets.Add(packet);
        Client?.PendingPackets.Add(packet);
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

    public static ClientSession GetClientByPlayerIndex(int playerIndex) {
        if (Clients.TryGetValue(playerIndex, out ClientSession client)) {
            return client;
        }
        return null;
    }

    public static ClientSession GetClientByGUID(Guid? guid) {
        if (Server != null) return Server.GetClientByGUID(guid);
        return Client != null ? Client.GetClientByGUID(guid) : null;
    }

    public static void AddClient(ClientSession client) {
        Server?.AddClient(client);
        Client?.AddClient(client);
    }

    public static ClientSession CreateClient(NetPeer peer, Guid guid, string dataId) =>
        Server.CreateClient(peer, guid, dataId);

    public static void Stop() {
        Server?.Stop();
        Client?.Stop();
    }

    public static void Update() {
        Client?.Update();
        Server?.Update();
    }
}


