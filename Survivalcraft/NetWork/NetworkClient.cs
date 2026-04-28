using System.Net;
using Engine;
using Game.NetWork.Packages;
using GameEntitySystem;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Game.NetWork;

public class Client {
    public bool IsLocalRemote { get; set; }
    public string Nickname { get; set; }
    public byte ID { get; internal set; }
    // 客户端获取到其他玩家的PlayerGuid是他们各自的TokenID，被混淆过了
    // 客户端自己的 PlayerGuid，以及服务端上所有玩家的 PlayerGuid 都未经混淆。
    // 以上的PlayerGuid指的是ComponentPlayer,PlayerData,Client中的PlayerGuid
    public Guid PlayerGuid { get; internal set; }
    public Guid TokenId { get; internal set; }
    public NetPeer Peer { get; internal set; }
    public ConnectionRequest Request { get; internal set; }
    public IPEndPoint IPPoint { get; internal set; }
    public SubsystemPlayers SubsystemPlayers => GameManager.Project.FindSubsystem<SubsystemPlayers>();
    public bool IsConnected => Peer != null && Peer.ConnectionState != ConnectionState.Disconnected;
    public ClientStage State { get; set; }
    public PlayerData PlayerData { get { return SubsystemPlayers.PlayersData.Find(p => p.ClientId == ID); } }

    /// <summary>
    /// 是否已加入连接成功列表
    /// </summary>
    public bool IsAddedToNetNode { get; private set; }
    // 服务器本身的Client
    public Client(Guid guid) {
        PlayerGuid = guid;
        State = ClientStage.Connected;
        TokenId = Guid.NewGuid();
        ID = 0;
    }

    /// <summary>
    /// 服务端创建新的Client
    /// </summary>
    /// <param name="netPeer">peer</param>
    /// <param name="id">客户端ID</param>
    /// <param name="tokenId">客户端tokenId</param>
    /// <param name="guid">客户端Guid</param>
    /// <param name="nickName">社区昵称</param>
    public Client(NetPeer netPeer, byte id, Guid tokenId, Guid guid, string nickName) {
        ID = id;
        PlayerGuid = guid;
        TokenId = tokenId;
        Nickname = nickName;
        Peer = netPeer;
        IsAddedToNetNode = true;
    }

    public Client(IPEndPoint iPEndPoint) {
        IPPoint = iPEndPoint;
        State = ClientStage.NotConnected;
    }

    public Client(ConnectionRequest request) {
        Request = request;
        IPPoint = request.RemoteEndPoint;
        State = ClientStage.NotConnected;
    }

    public Client(NetPeer peer) {
        Peer = peer;
        State = ClientStage.NotConnected;
        TokenId = Guid.NewGuid();
    }

    public override bool Equals(object obj) => obj is Client c && c.ID == ID;

    public override int GetHashCode() => PlayerGuid.GetHashCode();

    public static bool operator ==(Client a, Client b) {
        return (a is null && b is null) || (a is not null && b is not null && a.ID == b.ID);
    }

    public static bool operator !=(Client a, Client b) => !(a == b);
}

public class NetworkClient : INetworkNode, INetEventListener {
    public event Action<NetPeer, NetDataReader, DeliveryMethod> NetworkReceiveEvent;
    public event Action<NetPeer> PeerConnectedEvent;
    public event Action<NetPeer, DisconnectInfo> PeerDisconnectedEvent;
    public event Action<IPEndPoint, NetPacketReader, UnconnectedMessageType> NetworkReceiveUnconnectedEvent;
    public event Action<byte[], IPEndPoint> NetworkErrorEvent;
    public event Action<ConnectionRequest> ConnectionRequestEvent;
    public event Action<NetPeer, object> DeliveryEvent;

    public readonly NetManager _client;
    private readonly NetworkFragment m_chunkTransport;
    private readonly NetPacketTransport m_packetTransport;
    private readonly NetRuntimeController m_runtimeController;

    public Client Self { get; set; }
    public Client Server { get; set; }
    public NetworkStage CurrentStage { get; set; } = NetworkStage.NotConnected;
    public bool IsServer => CurrentStage == NetworkStage.Connected && Self == Server;
    public int ClientCount => Peers.Count() + 1;
    public Dictionary<int, Client> Clients { get; } = new Dictionary<int, Client>();
    public IEnumerable<Client> Peers => Clients.Values.Where(c => c != Self);
    public List<IPackage> PendingPackages { get; } = new List<IPackage>();
    public List<IPackage> PendingHandlePackages { get; } = new List<IPackage>();
    public Action<Client> OnClientStateChanged { get; set; }
    public Guid TokenId { get; set; }
    public bool isStopping { get; set; }
    public WorkType WorkType { get; set; }

    public NetManager NetManager => _client;

    public NetworkClient() {
        _client = new NetManager(this) {
            MaxConnectAttempts = 6,
            DisconnectTimeout = NetworkManager.DISCONNECT_TIMEOUT,
            UnconnectedMessagesEnabled = true,
            UpdateTime = 25,
            ReuseAddress = true
        };
        m_chunkTransport = new NetworkFragment(this);
        m_packetTransport = new NetPacketTransport(this, m_chunkTransport);
        m_runtimeController = new NetRuntimeController(this, m_chunkTransport, m_packetTransport);
    }

    public void ConnectServer(IPEndPoint ep, string passwd = "", string? onlineAccessToken = null) {
        try {
            isStopping = false;
            Log.Information($"connecting to server at {ep.Address}");
            Clients.Clear();
            _client.Start();
            TokenId = Guid.NewGuid();
            onlineAccessToken ??= SettingsManager.OnlineAccessToken;
            SendWriterFromPackage(new ConnectionRequestPackage(TokenId, "ProjectNet.Server_Version", string.Empty, onlineAccessToken, passwd, ModsManager.ModList), ep, false);
            NetworkReceiveEvent += OnNetworkReceivePackage;
            NetworkReceiveUnconnectedEvent += HanldeUnconnectedEvent;
            PeerDisconnectedEvent += OnPeerDisconnected;
            NetworkErrorEvent += HandleNetworkError;
            CurrentStage = NetworkStage.WaitForClientList;
            WorkType = WorkType.Client;
            Window.Frame += Update;
            Window.Closed += StopImmediate;
            OnClientStateChanged += ClientStateChanged;
        }
        catch {
            CurrentStage = NetworkStage.NotConnected;
        }
    }

    public void AddClient(Client c) {
        Clients[c.ID] = c;
    }

    public void RemoveClientImmediate(Client c, string reason = null) {
        if (c == Self) {
            Stop();
            return;
        }

        if (c.Peer != null && c.Peer.ConnectionState != ConnectionState.Disconnected) {
            if (string.IsNullOrEmpty(reason)) c.Peer.Disconnect();
            else c.Peer.Disconnect(NetDataWriter.FromString(reason));
        }

        if (c == Server) {
            Stop();
            CurrentStage = NetworkStage.NotConnected;
            return;
        }
        if (Clients.ContainsKey(c.ID)) {
            if (IsServer) {
                NetworkManager.QueuePackage(new ClientRemovePackage(c.ID));
            }
            c.State = ClientStage.NotConnected;
            OnClientStateChanged?.Invoke(c);
            Clients.Remove(c.ID);
        }
    }

    public Client GetClientByID(byte id) {
        if (Clients.ContainsKey(id))
            return Clients[id];
        return null;
    }

    public Client GetClientByGUID(Guid? guid) {
        return Clients.Values.FirstOrDefault(c => c.PlayerGuid == guid);
    }

    public Client GetClientByTokenID(Guid? guid) {
        return Clients.Values.FirstOrDefault(c => c.TokenId == guid);
    }

    public void StartLocal() {
        Clients.Clear();
        Self = new Client(new Guid(SettingsManager.OnlineAccessToken));
        AddClient(Self);
    }

    public void HanldeUnconnectedEvent(IPEndPoint ip, NetPacketReader reader, UnconnectedMessageType messageType) {
        m_packetTransport.HandleUnconnectedEvent(ip, reader, false);
    }

    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo) {
        m_packetTransport.HandlePeerDisconnected(peer, disconnectInfo);
    }

    public void OnNetworkReceivePackage(NetPeer peer, NetDataReader reader, DeliveryMethod deliveryMethod) {
        m_packetTransport.HandleNetworkReceive(peer, reader);
    }

    public virtual void HandleConnectionReject(IPEndPoint sender, NetDataReader reader, UnconnectedMessageType type) {
    }

    public void SendWriterFromPackage(IPackage package, IPEndPoint endPoint, bool isSend = true) {
        m_packetTransport.SendWriterFromPackage(package, endPoint, isSend);
    }

    public void SendWriterFromPackage(IPackage package, NetPeer netPeer, bool useDeliveryEvent = false) {
        m_packetTransport.SendWriterFromPackage(package, netPeer, useDeliveryEvent);
    }

    public void SendWriterFromPackage(IPackage package, ConnectionRequest request, bool reject) {
        m_packetTransport.SendWriterFromPackage(package, request, reject);
    }

    public void SendWriterFromPackages(IEnumerable<IPackage> packages, NetPeer netPeer = null, ConnectionRequest request = null, IPEndPoint iPEndPoint = null, bool useDeliveryEvent = false) {
        m_packetTransport.SendWriterFromPackages(packages, netPeer, request, iPEndPoint, useDeliveryEvent);
    }

    public void Stop() {
        isStopping = true;
    }

    public void StopImmediate() {
        m_runtimeController.StopImmediate();
    }

    public void Update() {
        m_runtimeController.Update();
    }

    public void CleanupAfterStop() {
        Window.Closed -= StopImmediate;
        Window.Frame -= Update;
        CurrentStage = NetworkStage.NotConnected;
        WorkType = WorkType.Local;
    }

    public byte[] BuildOutgoingPayload(IEnumerable<IPackage> packages, bool isServer) {
        return m_packetTransport.BuildOutgoingPayload(packages, isServer);
    }

    public void SendPayload(byte[] payload, NetPeer netPeer = null, ConnectionRequest request = null, IPEndPoint iPEndPoint = null, bool useDeliveryEvent = false) {
        m_chunkTransport.SendPayload(payload, netPeer, request, iPEndPoint, useDeliveryEvent);
    }

    public void CleanupChunkStates() {
        m_chunkTransport.CleanupStates();
    }

    public bool TryDecodePackages(NetDataReader reader, NetPeer peer, ConnectionRequest request, IPEndPoint endPoint, out List<IPackage> packages) {
        return m_packetTransport.TryDecodePackages(reader, peer, request, endPoint, out packages);
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
        NetworkManager.QueuePackage(new ClientStateChangePackage(Self.ID, ClientStage.ProjectLoaded));
    }

    private void HandleNetworkError(byte[] _, IPEndPoint endPoint) {
        Log.Information("Connection error " + endPoint);
        Stop();
    }
    void INetEventListener.OnPeerConnected(NetPeer peer) {
        PeerConnectedEvent?.Invoke(peer);
    }

    void INetEventListener.OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo) {
        PeerDisconnectedEvent?.Invoke(peer, disconnectInfo);
    }

    void INetEventListener.OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod) {
        var dataReader = new NetDataReader(reader.RawData, reader.Position, reader.AvailableBytes);
        NetworkReceiveEvent?.Invoke(peer, dataReader, deliveryMethod);
    }

    void INetEventListener.OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) {
        NetworkReceiveUnconnectedEvent?.Invoke(remoteEndPoint, reader, messageType);
    }

    void INetEventListener.OnNetworkError(IPEndPoint endPoint, System.Net.Sockets.SocketError socketError) {
        NetworkErrorEvent?.Invoke(null, endPoint);
    }

    void INetEventListener.OnConnectionRequest(ConnectionRequest request) {
        ConnectionRequestEvent?.Invoke(request);
    }

    void INetEventListener.OnNetworkLatencyUpdate(NetPeer peer, int latency) {
    }
}
