using System.Net;
using Engine;
using GameEntitySystem;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Game.Network;

public class Client {
    public string Nickname { get; set; }
    public byte ID { get; internal set; }
    public Guid NetworkGuid { get; internal set; }
    public NetPeer Peer { get; internal set; }
    public ConnectionRequest Request { get; internal set; }
    public IPEndPoint IPPoint { get; internal set; }
    public SubsystemPlayers SubsystemPlayers => GameManager.Project.FindSubsystem<SubsystemPlayers>();
    public bool IsConnected => Peer != null && Peer.ConnectionState != ConnectionState.Disconnected;
    public NetworkState State { get; set; } = NetworkState.NotConnected;
    public PlayerData PlayerData { get { return NetworkService.GetPlayerByClient(this); } }

    public Client() { }

    public Client(Guid guid) {
        NetworkGuid = guid;
        State = NetworkState.Connected;
        ID = 0;
    }

    public Client(NetPeer netPeer, byte id, Guid guid, string nickName) {
        ID = id;
        NetworkGuid = guid;
        Nickname = nickName;
        Peer = netPeer;
    }

    public override bool Equals(object obj) => obj is Client c && c.ID == ID;

    public override int GetHashCode() => NetworkGuid.GetHashCode();

    public static bool operator ==(Client a, Client b) {
        return (a is null && b is null) || (a is not null && b is not null && a.ID == b.ID);
    }

    public static bool operator !=(Client a, Client b) => !(a == b);
}


public class NetworkClient : INetEventListener {
    public event Action<NetPeer, NetDataReader, DeliveryMethod> NetworkReceiveEvent;
    public event Action<NetPeer> PeerConnectedEvent;
    public event Action<NetPeer, DisconnectInfo> PeerDisconnectedEvent;
    public event Action<IPEndPoint, NetPacketReader, UnconnectedMessageType> NetworkReceiveUnconnectedEvent;
    public event Action<byte[], IPEndPoint> NetworkErrorEvent;
    public event Action<ConnectionRequest> ConnectionRequestEvent;
    public event Action<NetPeer, object> DeliveryEvent;

    public Client Server { get; set; }
    public Dictionary<int, Client> Clients { get; } = new Dictionary<int, Client>();
    public List<Packet> PendingPackets { get; } = new List<Packet>();

    private List<Packet> m_bufferedPackets = new List<Packet>();
    public Action<Client> OnNetworkStateChanged { get; set; }

    public Client Self { get; set; }
    public NetManager NetManager { get; set; }
    public PacketFragment PackageFragment { get; set; }

    public NetworkClient() {
        NetManager = new NetManager(this) {
            MaxConnectAttempts = 6,
            DisconnectTimeout = 10000,
            UnconnectedMessagesEnabled = true,
            UpdateTime = 25,
            ReuseAddress = true
        };
        PackageFragment = new PacketFragment(NetManager);
    }

    public bool ConnectServer(IPEndPoint ep, string passwd = "", string? onlineAccessToken = null) {
        try {
            Log.Information($"connecting to server at {ep.Address}");
            Clients.Clear();
            NetManager.Start();
            onlineAccessToken ??= SettingsManager.OnlineAccessToken;
            Self = new Client(new Guid(onlineAccessToken)) { State = NetworkState.NotConnected };
            SendWriterFromPacket(new ConnectionRequestPacket("ProjectNet.Server_Version", string.Empty, onlineAccessToken, passwd, ModsManager.ModList), ep, false);
        }
        catch {
            Self?.State = NetworkState.NotConnected;
            NetManager.Stop();
        }
        return NetManager.IsRunning;
    }

    public void AddClient(Client c) {
        Clients[c.ID] = c;
    }

    public Client GetClientByGUID(Guid? guid) {
        return Clients.Values.FirstOrDefault(c => c.NetworkGuid == guid);
    }

    public void SendWriterFromPacket(Packet packet, IPEndPoint endPoint, bool isSend = true) {
        NetworkHelper.SendWriterFromPacket(PackageFragment, packet, endPoint, isSend);
    }

    public void Update() {
        if (NetManager.IsRunning) {
            // Client only sends to server (ID 0 usually)
            if (Clients.TryGetValue(0, out Client serverClient) && serverClient.IsConnected) {
                byte[] payload = NetworkHelper.BuildPayloadForClient(PendingPackets, serverClient, true, false);
                if (payload != null) PackageFragment.SendPayload(payload, serverClient.Peer);
            }
            PendingPackets.Clear();

            // Process buffered packets
            while (m_bufferedPackets.Count > 0) {
                var packet = m_bufferedPackets[0];
                if (Self == null || packet.MinNeedState > Self.State) break;

                packet.Handle(false);
                m_bufferedPackets.RemoveAt(0);
            }

            PackageFragment.CleanupStates();
            NetManager.PollEvents();
        }
    }

    public void Stop() {
        if (NetManager.IsRunning) {
            NetManager.Stop(true);
        }
        m_bufferedPackets.Clear();
    }

    public void OnPeerConnected(NetPeer peer) {
        PeerConnectedEvent?.Invoke(peer);
    }

    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo) {
        PeerDisconnectedEvent?.Invoke(peer, disconnectInfo);
        Log.Information("Disconnect by " + disconnectInfo);
        Stop();
    }

    public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod) {
        if (!NetworkHelper.TryDecodePackets(Clients.Values,
            PackageFragment, reader, peer, null, null, out List<Packet> packets)) {
            return;
        }
        foreach (var packet in packets) {
            if (m_bufferedPackets.Count > 0 || Self == null || packet.MinNeedState > Self.State) {
                m_bufferedPackets.Add(packet);
            }
            else {
                packet.Handle(false);
            }
        }
    }

    public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) {
        NetworkReceiveUnconnectedEvent?.Invoke(remoteEndPoint, reader, messageType);
        NetworkHelper.HandleUnconnectedEvent(Clients.Values, false, PackageFragment, remoteEndPoint, reader, false);
    }

    public void OnNetworkError(IPEndPoint endPoint, System.Net.Sockets.SocketError socketError) {
        Log.Information("Connection error " + endPoint);
        NetworkErrorEvent?.Invoke(null, endPoint);
        Stop();
    }

    public void OnConnectionRequest(ConnectionRequest request) {
        ConnectionRequestEvent?.Invoke(request);
    }

    public void OnNetworkLatencyUpdate(NetPeer peer, int latency) {
    }

    internal PlayerData GetPlayerByClient(Client client) {
        if (client == null) return null;
        var subsystemPlayers = GameManager.Project?.FindSubsystem<SubsystemPlayers>();
        if (subsystemPlayers != null) {
            foreach (var playerData in subsystemPlayers.PlayersData) {
                if (playerData.NetworkGuid == client.NetworkGuid) {
                    return playerData;
                }
            }
        }
        return null;
    }
}
