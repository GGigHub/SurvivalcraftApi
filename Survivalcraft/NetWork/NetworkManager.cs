using System.IO.Compression;
using System.Net;
using System.Net.NetworkInformation;
using System.Xml.Linq;
using Engine;
using Engine.Graphics;
using Game.Network;
using GameEntitySystem;
using LiteNetLib;
using LiteNetLib.Utils;
using TemplatesDatabase;
#pragma warning disable CA1416

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
        lock (Current.PendingPackets) {
            Current.PendingPackets.Add(packet);
        }
    }

    public static PacketStreamReader GetReader(NetDataReader reader) {
        return new PacketStreamReader(reader.GetBytesWithLength());
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

                // 物理网卡类型检�?
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

    public static Client GetClientByID(byte id) => Current.GetClientByID(id);

    public static Client GetClientByGUID(Guid? guid) => Current.GetClientByGUID(guid);

    public static void AddClient(Client client) => Current.AddClient(client);

    public static Client CreateClient(NetPeer peer, Guid tokenId, Guid guid, string dataId, string nickname) =>
        Server.CreateClient(peer, tokenId, guid, dataId, nickname);

    public static void Stop() => Current.Stop();

    public static bool IsValidIPAddress(string ip) {
        return IPAddress.TryParse(ip, out _);
    }

    public static bool IsValidPort(int port) {
        return port > 0 && port <= 65535;
    }

    internal static void HandleUnconnectedEvent(INetworkNode node, PacketFragment chunkTransport, IPEndPoint remoteEndPoint, NetPacketReader reader, bool fromBroadcast) {
        if (node.CurrentStage == NetworkStage.NotConnected) return;
        var dataReader = new NetDataReader(reader.RawData, reader.Position, reader.AvailableBytes);
        try {
            if (!TryDecodePackets(node, chunkTransport, dataReader, null, null, remoteEndPoint, out List<Packet> packets)) {
                return;
            }

            foreach (var packet in packets) {
                packet.From.IsLocalRemote = fromBroadcast;
                try {
                    packet.Handle(node.IsServer);
                }
                catch (Exception e) {
                    Log.Error(string.Format("[{0}]{1}", packet.GetType().Name, e.Message));
                }
            }
        }
        finally {
            reader.Recycle();
        }
    }

    internal static void HandlePeerDisconnected(INetworkNode node, NetPeer peer, DisconnectInfo disconnectInfo) {
        string reason = disconnectInfo.Reason.ToString();
        if (disconnectInfo.AdditionalData != null && disconnectInfo.AdditionalData.TryGetString(out string extra)) {
            Log.Information("----Additional message----");
            Log.Information(extra);
            Log.Information("----End of message----");
            reason = extra;
        }

        if (node.WorkType == WorkType.Client) {
            node.Stop();
            return;
        }

        Client client = peer.Tag as Client;
        if (client == null) {
            return;
        }

        client.State = ClientStage.NotConnected;
        node.RemoveClientImmediate(client);
        node.OnClientStateChanged?.Invoke(client);
        if (node is NetworkServer server) {
            server.HandleDeliveryEvent(null, null);
        }
    }

    internal static void HandleNetworkReceive(INetworkNode node, PacketFragment chunkTransport, NetPeer peer, NetDataReader reader) {
        if (node.CurrentStage == NetworkStage.NotConnected) return;
        Client fromClient = peer.Tag as Client;
        if (!TryDecodePackets(node, chunkTransport, reader, peer, null, null, out List<Packet> packets)) {
            return;
        }

        packets.InsertRange(0, node.PendingHandlePackets);
        foreach (var packet in packets) {
            try {
                packet.Handle(node.IsServer);
            }
            catch (Exception e) {
                Log.Error(string.Format("[{0}]{1}", packet.GetType().Name, e.Message));
            }
        }
    }

    internal static void SendWriterFromPacket(INetworkNode node, PacketFragment chunkTransport, Packet packet, IPEndPoint endPoint, bool isSend = true) {
        SendWriterFromPackets(node, chunkTransport, [packet], null, null, endPoint, isSend);
    }

    internal static void SendWriterFromPacket(INetworkNode node, PacketFragment chunkTransport, Packet packet, NetPeer netPeer, bool useDeliveryEvent = false) {
        SendWriterFromPackets(node, chunkTransport, [packet], netPeer, null, null, useDeliveryEvent);
    }

    internal static void SendWriterFromPacket(INetworkNode node, PacketFragment chunkTransport, Packet packet, ConnectionRequest request, bool reject) {
        if (reject) request.Reject();
        SendWriterFromPackets(node, chunkTransport, [packet], null, request, null);
    }

    internal static void SendWriterFromPackets(INetworkNode node, PacketFragment chunkTransport, IEnumerable<Packet> packets, NetPeer netPeer = null, ConnectionRequest request = null, IPEndPoint iPEndPoint = null, bool useDeliveryEvent = false) {
        byte[] payload = BuildOutgoingPayload(packets, node.IsServer);
        chunkTransport.SendPayload(payload, netPeer, request, iPEndPoint, useDeliveryEvent);
    }

    internal static byte[] BuildOutgoingPayload(IEnumerable<Packet> packets, bool isServer) {
        PacketStreamWriter writer = new();
        foreach (var packet in packets) {
            writer.Write(PacketFragment.VerifyByte);
            writer.Write(packet.ID);
            packet.Serialize(new PacketSerializer(writer));
        }
        return writer.Data();
    }

    internal static bool TryDecodePackets(INetworkNode node, PacketFragment chunkTransport, NetDataReader reader, NetPeer peer, ConnectionRequest request, IPEndPoint endPoint, out List<Packet> packets) {
        packets = null;
        if (chunkTransport.TryReadPayload(reader, peer, request, endPoint, out byte[] payload, out bool consumedChunkFrame)) {
            if (payload == null) {
                return false;
            }
            packets = PacketManager.DecodePacketsFromPayload(node, payload, peer, request, endPoint);
            return true;
        }

        if (consumedChunkFrame) {
            return false;
        }

        packets = PacketManager.DecodePackets(node, reader, peer, request, endPoint);
        return true;
    }

    internal static int SendWriterFromPacket(NetManager netManager, IEnumerable<Packet> packets, IPEndPoint endPoint) {
        byte[] payload = BuildOutgoingPayload(packets, false);
        PacketFragment.SendPayload(netManager, payload, endPoint);
        return payload.Length;
    }

    public static void Update() {
        Client?.Update();
        Server?.Update();
    }

    public static byte[] BuildPayloadForClient(INetworkNode node, Client client, bool respectMinState) {
        List<Packet> packets = new List<Packet>();
        foreach (Packet packet in node.PendingPackets) {
            if (packet.To != null && packet.To != client) continue;
            if (packet.Except != null && packet.Except == client) continue;
            if (respectMinState && client.State < packet.MinNeedState && node.WorkType != WorkType.Client) continue;
            packets.Add(packet);
        }
        return packets.Count > 0 ? BuildOutgoingPayload(packets, node.IsServer) : null;
    }
}


