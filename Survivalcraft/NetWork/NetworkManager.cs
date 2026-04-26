using System.IO.Compression;
using System.Net;
using System.Net.NetworkInformation;
using System.Xml.Linq;
using Engine;
using Engine.Graphics;
using Game.NetWork.Packages;
using GameEntitySystem;
using LiteNetLib.Utils;
using TemplatesDatabase;
#pragma warning disable CA1416

namespace Game.NetWork;

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
    private static INetworkNode Current => (INetworkNode)Server ?? Client;

    public static Texture2D BlockTexture;

    public static bool IsOfflineMode;
    public static WorkType WorkType => Current.WorkType;
    public static Client Self => Current.Self;
    public static Dictionary<int, Client> Clients => Current.Clients;
    public static IEnumerable<Client> Peers => Current.Peers;
    public static int ClientCount => Current.ClientCount;
    public static NetworkStage CurrentStage => Current.CurrentStage;
    public static bool IsNetRunning => Current.NetManager.IsRunning;
    public static Action<Client> OnClientStateChanged {
        get => Current.OnClientStateChanged;
        set => Current.OnClientStateChanged = value;
    }

    /// <summary>
    /// 获取玩家的有效模式
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
        var net = new NetworkServer();
        ResetNet(net);
        return net.StartServer(SettingsManager.ServerPort);
    }

    public static void ConnectServer(IPEndPoint ep, string passwd = "", string? onlineAccessToken = null) {
        var net = new NetworkClient();
        ResetNet(net);
        net.ConnectServer(ep, passwd, onlineAccessToken);
    }

    public static void QueuePackage(IPackage package) {
        lock (Current.PendingPackages) {
            Current.PendingPackages.Add(package);
        }
    }

    private static void ResetNet(INetworkNode networkBase) {
        if (Current != null && !ReferenceEquals(Current, networkBase)) {
            Current.StopImmediate();
        }
        Client = networkBase as NetworkClient;
        Server = networkBase as NetworkServer;
    }

    public static PackageStreamReader GetReader(NetDataReader reader) {
        return new PackageStreamReader(reader.GetBytesWithLength());
    }

    public static string GetInnerIp() {
        try {
            var validTypes = new List<NetworkInterfaceType>
            {
                    NetworkInterfaceType.Ethernet,
                    NetworkInterfaceType.Ethernet3Megabit,
                    NetworkInterfaceType.FastEthernetFx,
                    NetworkInterfaceType.FastEthernetT,
                    NetworkInterfaceType.GigabitEthernet,
                    NetworkInterfaceType.Wireless80211
                };

            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces()) {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                // 物理网卡类型检测
                if (validTypes.Contains(ni.NetworkInterfaceType)) {
                    foreach (var ip in ni.GetIPProperties().UnicastAddresses) {
                        if (ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) {
                            return ip.Address.ToString();
                        }
                    }
                }
            }

            // 备用方案：获取首个非虚拟IPv4地址
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Where(ip => ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                .Select(ip => ip.Address.ToString())
                .FirstOrDefault() ?? "127.0.0.1";
        }
        catch {
            return "127.0.0.1";
        }
    }

    public static byte[] GetNowProject(Project project, Client client) {
        ValuesDictionary rootNode = CreateProjectRootNode(project);
        ReplacePlayerGUIDsWithTokenIds(rootNode, client);
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

    private static void ReplacePlayerGUIDsWithTokenIds(ValuesDictionary rootNode, Client client) {
        try {
            var playersSubsystem = rootNode?
                .GetValue("Subsystems", new ValuesDictionary())
                .GetValue("Players", null as ValuesDictionary);

            if (playersSubsystem == null) return;

            var playersList = playersSubsystem.GetValue("Players", null as ValuesDictionary);

            if (playersList != null) {
                foreach (var playerVD in playersList.Values.OfType<ValuesDictionary>()) {
                    Guid originalGuid = playerVD.GetValue("PlayerGUID", Guid.Empty);
                    if (originalGuid != Guid.Empty) {
                        ReplaceGUIDIfNeeded(playerVD, client, originalGuid);
                    }
                }
            }
        }
        catch (Exception e) {
            Log.Information($"ReplacePlayerGUIDsWithTokenIds error: {e.Message}");
        }
    }

    private static void ReplaceGUIDIfNeeded(ValuesDictionary playerVD, Client currentClient, Guid originalGuid) {
        var targetClient = GetClientByGUID(originalGuid);

        if (targetClient != null && targetClient.PlayerGuid != currentClient.PlayerGuid) {
            playerVD.SetValue("PlayerGUID", targetClient.TokenId);
        }
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

    public static Client GetClientByID(byte id) => Current.GetClientByID(id);

    public static Client GetClientByGUID(Guid? guid) => Current.GetClientByGUID(guid);

    public static void AddClient(Client client) => Current.AddClient(client);

    public static Client CreateClient(LiteNetLib.NetPeer peer, Guid tokenId, Guid guid, string dataId, string nickname) =>
        Server.CreateClient(peer, tokenId, guid, dataId, nickname);

    public static void Stop() => Current.Stop();

    public static void StopImmediate() => Current.StopImmediate();

    public static bool IsValidIPAddress(string ip) {
        return IPAddress.TryParse(ip, out _);
    }

    public static bool IsValidPort(int port) {
        return port > 0 && port <= 65535;
    }
}
