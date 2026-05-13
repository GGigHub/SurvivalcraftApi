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

public static class NetworkManager {
    public static NetworkServer Server { get; private set; }
    public static NetworkClient Client { get; private set; }

    public static Texture2D BlockTexture;

    public static bool IsOfflineMode;
    public static Client Self => Server?.Self ?? Client?.Self;
    public static Dictionary<int, Client> Clients => Server != null ? Server.Clients : Client != null ? Client.Clients : [];
    public static IEnumerable<Client> Peers => Server != null ? Server.Peers : Client != null ? Client.Clients.Values.Where(c => c != Client.Self) : [];
    public static bool IsServer => Server != null && Server.NetManager.IsRunning;
    public static bool IsClient => Client != null && Client.NetManager.IsRunning;
    public static bool IsMultiplayer => IsServer || IsClient;

    public static ComponentPlayer MainPlayer {
        get {
            if (GameManager.Project != null && Self != null) {
                return GameManager.Project.FindSubsystem<SubsystemPlayers>().ComponentPlayers.FirstOrDefault(p => p.PlayerData.NetworkGuid == Self.NetworkGuid);
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

    public static Client GetClientByGUID(Guid? guid) {
        if (Server != null) return Server.GetClientByGUID(guid);
        return Client != null ? Client.GetClientByGUID(guid) : null;
    }

    public static void AddClient(Client client) {
        Server?.AddClient(client);
        Client?.AddClient(client);
    }

    public static Client CreateClient(NetPeer peer, Guid guid, string dataId, string nickname) =>
        Server.CreateClient(peer, guid, dataId, nickname);

    public static void Stop() {
        Server?.Stop();
        Client?.Stop();
    }

    public static void Update() {
        Client?.Update();
        Server?.Update();
    }
}


