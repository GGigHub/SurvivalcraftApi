using System.Net;
using Engine;
using GameEntitySystem;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Game.Network;

public class ONetworkClient : INetEventListener {
    public event Action<NetPeer, NetDataReader, DeliveryMethod> NetworkReceiveEvent;
    public event Action<NetPeer> PeerConnectedEvent;
    public event Action<NetPeer, DisconnectInfo> PeerDisconnectedEvent;
    public event Action<IPEndPoint, NetPacketReader, UnconnectedMessageType> NetworkReceiveUnconnectedEvent;
    public event Action<byte[], IPEndPoint> NetworkErrorEvent;
    public event Action<ConnectionRequest> ConnectionRequestEvent;
    public event Action<NetPeer, object> DeliveryEvent;

    public ClientSession Server { get; set; }
    public NetPeer ServerPeer { get; internal set; }
    public Dictionary<int, ClientSession> Clients { get; } = new Dictionary<int, ClientSession>();
    public List<Packet> PendingPackets { get; } = new List<Packet>();

    private List<Packet> m_bufferedPackets = new List<Packet>();
    public Action<ClientSession> OnNetworkStateChanged { get; set; }

    public ClientSession Self { get; set; }
    public NetManager NetManager { get; set; }
    public PacketFragment PackageFragment { get; set; }

    public ONetworkClient() {
        NetManager = new NetManager(this) {
            MaxConnectAttempts = 6,
            DisconnectTimeout = 10000,
            UnconnectedMessagesEnabled = true,
            UpdateTime = 25,
            ReuseAddress = true
        };
        PackageFragment = new PacketFragment();
    }

    public bool ConnectServer(IPEndPoint ep, string passwd = "", string? onlineAccessToken = null) {
        try {
            Log.Information($"connecting to server at {ep.Address}");
            Clients.Clear();
            NetManager.Start();
            onlineAccessToken ??= SettingsManager.OnlineAccessToken;
            Self = new ClientSession { NetworkGuid = new Guid(onlineAccessToken), NetworkState = NetworkState.NotConnected };
            SendWriterFromPacket(new ConnectionRequestPacket("ProjectNet.Server_Version", string.Empty, onlineAccessToken, passwd, ModsManager.ModList), ep, false);
        }
        catch {
            if (Self != null) Self.NetworkState = NetworkState.NotConnected;
            NetManager.Stop();
        }
        return NetManager.IsRunning;
    }

    public void AddClient(ClientSession c) {
        Clients[c.PlayerIndex] = c;
    }

    public ClientSession GetClientByGUID(Guid? guid) {
        return Clients.Values.FirstOrDefault(c => c.NetworkGuid == guid);
    }

    public void SendWriterFromPacket(Packet packet, IPEndPoint endPoint, bool isSend = true) {
        NetworkHelper.SendWriterFromPacket(NetManager, packet, endPoint, isSend);
    }

    public void Update() {
        if (NetManager.IsRunning) {
            // Client only sends to server (ID 0 usually)
            if (Clients.TryGetValue(0, out ClientSession serverClient) && serverClient.NetworkState != NetworkState.NotConnected && ServerPeer != null) {
                byte[] payload = NetworkHelper.BuildPayloadForClient(PendingPackets, serverClient, true, false);
                if (payload != null) {
                    foreach (var chunk in PacketFragment.FragmentPayload(payload)) {
                        ServerPeer.Send(chunk, DeliveryMethod.ReliableOrdered);
                    }
                }
            }
            PendingPackets.Clear();

            // Process buffered packets
            while (m_bufferedPackets.Count > 0) {
                var packet = m_bufferedPackets[0];
                if (Self == null || packet.MinNeedState > Self.NetworkState) break;

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
        ServerPeer = peer;
        PeerConnectedEvent?.Invoke(peer);
    }

    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo) {
        ServerPeer = null;
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
            if (m_bufferedPackets.Count > 0 || Self == null || packet.MinNeedState > Self.NetworkState) {
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

    // GetPlayerByClient removed
}
