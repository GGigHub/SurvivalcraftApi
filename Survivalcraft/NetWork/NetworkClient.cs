using System.Net;
using Engine;
using GameEntitySystem;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Game.Network;

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
    public Action<Client> OnClientStateChanged { get; set; }

    public Client Self { get; set; }
    public NetworkStage CurrentStage { get; set; } = NetworkStage.NotConnected;
    public WorkType WorkType { get; set; }
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

    public void ConnectServer(IPEndPoint ep, string passwd = "", string? onlineAccessToken = null) {
        try {
            Log.Information($"connecting to server at {ep.Address}");
            Clients.Clear();
            NetManager.Start();
            onlineAccessToken ??= SettingsManager.OnlineAccessToken;
            SendWriterFromPacket(new ConnectionRequestPacket("ProjectNet.Server_Version", string.Empty, onlineAccessToken, passwd, ModsManager.ModList), ep, false);
            CurrentStage = NetworkStage.WaitForClientList;
            WorkType = WorkType.Client;
            OnClientStateChanged += ClientStateChanged;
        }
        catch {
            CurrentStage = NetworkStage.NotConnected;
        }
    }

    public void AddClient(Client c) {
        Clients[c.ID] = c;
    }

    public Client GetClientByGUID(Guid? guid) {
        return Clients.Values.FirstOrDefault(c => c.PlayerGuid == guid);
    }

    public void SendWriterFromPacket(Packet packet, IPEndPoint endPoint, bool isSend = true) {
        NetworkHelper.SendWriterFromPacket(PackageFragment, packet, endPoint, isSend);
    }

    private void ClientStateChanged(Client c) {
        var subsystemPlayers = GameManager.Project.FindSubsystem<SubsystemPlayers>();

        if (c.State != ClientStage.NotConnected) {
            return;
        }

        var playerData = subsystemPlayers.PlayersData.Find(pd => pd.PlayerGUID == c.PlayerGuid);
        if (playerData != null && !playerData.IsMainPlayer) {
            subsystemPlayers.MakePlayerOffline(playerData.PlayerGUID);
        }
        SettingsManager.VisibilityRange = Math.Min(256, SettingsManager.VisibilityRange);
        NetworkManager.QueuePacket(new ClientStateChangePacket(Self.ID, ClientStage.ProjectLoaded));
    }

    public void Update() {
        if (NetManager.IsRunning) {
            // Client only sends to server (ID 0 usually)
            if (Clients.TryGetValue(0, out Client serverClient) && serverClient.IsConnected) {
                byte[] payload = NetworkHelper.BuildPayloadForClient(PendingPackets, serverClient, false, false);
                if (payload != null) PackageFragment.SendPayload(payload, serverClient.Peer);
            }
            PendingPackets.Clear();
            PackageFragment.CleanupStates();
            NetManager.PollEvents();
        }
    }

    public void Stop() {
        if (NetManager.IsRunning) {
            NetManager.Stop(true);
        }
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
        packets.ForEach(packet => packet.Handle(false));
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
}
