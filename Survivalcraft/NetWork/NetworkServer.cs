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

    public string CommunityAccountId { get; set; } = "-1";

    public ClientStage State { get; set; }

    public PlayerData PlayerData { get { return SubsystemPlayers.PlayersData.Find(p => p.ClientId == ID); } }

    public Entity CachePlayerEntity { get; set; }

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
    /// <param name="dataId">社区账号ID</param>
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
        return a is null && b is null || !(a is null) && !(b is null) && a.ID == b.ID;
    }

    public static bool operator !=(Client a, Client b) => !(a == b);

    public override string ToString() => $"Client : {{ID : {ID}, GUID : {PlayerGuid}}}";
}

public class NetworkServer : INetworkNode, INetEventListener {
    public event Action<NetPeer, NetDataReader, DeliveryMethod> NetworkReceiveEvent;
    public event Action<NetPeer> PeerConnectedEvent;
    public event Action<NetPeer, DisconnectInfo> PeerDisconnectedEvent;
    public event Action<IPEndPoint, NetPacketReader, UnconnectedMessageType> NetworkReceiveUnconnectedEvent;
    public event Action<byte[], IPEndPoint> NetworkErrorEvent;
    public event Action<ConnectionRequest> ConnectionRequestEvent;
    public event Action<NetPeer, object> DeliveryEvent;

    public readonly NetManager netManager;
    private readonly NetworkFragment m_chunkTransport;
    private readonly NetPacketTransport m_packetTransport;
    private readonly NetRuntimeController m_runtimeController;
    private Action<IEnumerable<IPackage>> m_onRecieve;

    public Client Self { get; set; }
    public Client Server { get; set; }
    public string Error { get; private set; }
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
    public long sec_length { get; set; }
    public long sec_length_total { get; set; }
    public long sec_total { get; set; }
    public bool AllowHandle { get; set; } = true;
    public WorkType WorkType { get; set; }
    NetManager INetworkNode.netManager => netManager;

    public NetPeer pendingPeer = null;
    public HashSet<byte> agreeOnPendingPeer = new HashSet<byte>();
    internal Client PendingClient => (Client)pendingPeer?.Tag;
    internal DateTime? pendingStartUtc;
    internal const double PendingJoinTimeoutSeconds = 2.0;
    internal readonly Dictionary<Client, string> ClientsToRemove = new Dictionary<Client, string>();

    public NetworkServer() {
        netManager = new NetManager(this) {
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

    public bool StartServer(int port) {
        if (netManager.IsRunning) {
            StopImmediate();
        }
        try {
            isStopping = false;
            StartLocal();
            if (netManager.Start(port)) {
                Log.Information($"Server started successfully on port {netManager.LocalPort}");
                Window.Frame += Update;
                Window.Closed += StopImmediate;
                ConnectionRequestEvent += OnConnectionRequest;
                NetworkReceiveUnconnectedEvent += HanldeUnconnectedEvent;
                NetworkReceiveEvent += OnNetworkReceivePackage;
                PeerDisconnectedEvent += OnPeerDisconnected;
                netManager.PollEvents();
                CurrentStage = NetworkStage.Connected;
                Server = Self;
                WorkType = WorkType.Server;
                OnClientStateChanged += ClientStateChanged;
                Project.EntityAdded += (_, arg) => {
                    foreach (Client client in Clients.Values.Where(c => c != Self)) {
                        NetworkManager.QueuePackage(new EntityPackage(arg.Entity) { To = client });
                    }
                };
                Project.EntityRemoved += (_, arg) => {
                    NetworkManager.QueuePackage(new EntityPackage(arg.Entity.Id));
                };
                TurnOnPackageHandle();
            }
            else {
                CurrentStage = NetworkStage.NotConnected;
                Stop();
            }
        }
        catch (Exception e) {
            Log.Error(e.Message);
        }
        return CurrentStage == NetworkStage.Connected;
    }

    public void HandleDeliveryEvent(NetPeer peer, object userData) {
        if (userData is NetPeer netPeer && netPeer.Tag is Client client) {
            agreeOnPendingPeer.Remove(client.ID);
#if DEBUG
            Log.Information($"Client[{client.ID}] acknowledged pending client [{PendingClient.ID}]");
#endif
        }
        if (agreeOnPendingPeer.Count == 0 && PendingClient != null) {
            AddClient(PendingClient);
            NetworkManager.QueuePackage(new ClientPackage(Clients.Values) { To = PendingClient });
#if DEBUG
            Log.Information($"Client[{PendingClient.ID}] join flow completed.");
#endif
            pendingPeer = null;
            pendingStartUtc = null;
        }
    }

    public void OnConnectionRequest(ConnectionRequest request) {
        Log.Information("Received connection request " + request.RemoteEndPoint);

        if (GameManager.Project == null) {
            SendWriterFromPackage(new ConnectionRejectPackage("Please wait until server startup is complete before joining."), request, true);
        }
        else if (PendingClient != null) {
            Log.Information($"Too Many Request And Reject Current Request:{request.RemoteEndPoint}");
            Log.Information($"Current Pending Client:{PendingClient}");
            SendWriterFromPackage(new ConnectionRejectPackage($"Server is handling another pending join. Waiting client confirmations: {agreeOnPendingPeer.Count}"), request, true);
        }
        else {
            try {
                ConnectionRequestPackage requestPackage = PackageManager.DecodePackage<ConnectionRequestPackage>(this, request.Data, null, request);
                requestPackage.Handle(true);
            }
            catch (Exception e) {
                SendWriterFromPackage(new ConnectionRejectPackage($"Invalid connection request packet: {e.Message}"), request, true);
                Log.Information("ConnectionRequestEvent Error:" + e.StackTrace);
            }
        }
    }

    public Client CreateClient(NetPeer peer, Guid tokenId, Guid guid, string dataId, string nickname) {
        if (!IsServer) throw new InvalidOperationException("creating client as non-server");
        foreach (var p in Peers) {
            if (p.PlayerGuid == guid) RemoveClient(p, "Duplicate account login.");
        }
        var c = new Client(peer, FindUnusedIndex(), tokenId, guid, nickname) {
            CommunityAccountId = dataId
        };
        peer.Tag = c;
        return c;
    }

    public void RemoveClient(Client c, string reason = null) {
        if (ClientsToRemove.ContainsKey(c)) {
            if (string.IsNullOrEmpty(reason))
                ClientsToRemove[c] += '\n' + reason;
        }
        else {
            ClientsToRemove[c] = reason ?? string.Empty;
        }
    }

    public void RemoveAllClients(string reason = null) {
        var cs = new List<Client>(Clients.Values);
        foreach (var c in cs) {
            if (c == Self) continue;
            if (c.Peer != null && c.Peer.ConnectionState != ConnectionState.Disconnected) {
                if (reason != null) c.Peer.Disconnect(NetDataWriter.FromString(reason));
                else c.Peer.Disconnect();
            }
            Clients.Remove(c.ID);
            c.State = ClientStage.NotConnected;
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
                NetworkManager.QueuePackage(new ClientPackage(c.ID));
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

    public void AddPendingHandlePackage(IPackage package) {
        m_packetTransport.AddPendingHandlePackage(package);
    }

    public void OnNetworkReceivePackage(NetPeer peer, NetDataReader reader, DeliveryMethod deliveryMethod) {
        m_packetTransport.HandleNetworkReceive(peer, reader);
    }

    public virtual void HandleConnectionReject(IPEndPoint sender, NetDataReader reader, UnconnectedMessageType type) {
    }

    public void TurnOnPackageHandle() {
        m_packetTransport.TurnOnPackageHandle();
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

    public void Stop(string error = null) {
        isStopping = true;
        Error = error;
    }

    public void StopImmediate() {
        m_runtimeController.StopImmediate();
    }

    public void Update() {
        m_runtimeController.Update();
    }

    public bool HasReceiveHandlers() {
        return m_onRecieve != null;
    }

    public void DispatchReceivedPackages(IEnumerable<IPackage> packages) {
        m_onRecieve?.Invoke(packages);
    }

    public void ClearTransientHandlers() {
        m_onRecieve = null;
        OnClientStateChanged = null;
    }

    public void CleanupAfterStop() {
        Window.Closed -= StopImmediate;
        Window.Frame -= Update;
        ClearConnectionRequestEvent();
        ClearNetworkReceiveUnconnectedEvent();
        ClearNetworkReceiveEvent();
        ClearNetworkErrorEvent();
        ClearPeerDisconnectedEvent();
        ClearDeliveryEvent();
        ClearPeerConnectedEvent();
        ClearTransientHandlers();
        CurrentStage = NetworkStage.NotConnected;
        WorkType = WorkType.Local;
        if (Error != null) {
            DialogsManager.Confirm(Error, btn => {
                ScreensManager.SwitchScreen("MainMenu");
            });
        }
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

    public void AddReceiveHandler(Action<IEnumerable<IPackage>> handler) {
        m_onRecieve += handler;
    }

    public void RemoveReceiveHandler(Action<IEnumerable<IPackage>> handler) {
        m_onRecieve -= handler;
    }

    private byte FindUnusedIndex() {
        for (byte i = 0; i < 255; i++) {
            if (!Clients.ContainsKey(i)) return i;
        }
        throw new Exception("Server is full.");
    }

    private void ClientStateChanged(Client c) {
        var subsystemPlayers = GameManager.Project.FindSubsystem<SubsystemPlayers>();
        var subsystemGameInfo = GameManager.Project.FindSubsystem<SubsystemGameInfo>();

        NetworkManager.QueuePackage(new ClientPackage(c.ID, c.State) { Except = c });

        switch (c.State) {
            case ClientStage.Connected: {
                    byte[] textureData = null;
                    if (subsystemPlayers.MakePlayerOnline(c.PlayerGuid, out PlayerData playerData, out Entity entity)) {
                        c.CachePlayerEntity = entity;
                        foreach (KeyValuePair<int, Client> client in Clients) {
                            if (client.Value != Self && client.Value != c) {
                                NetworkManager.QueuePackage(new PlayerDataPackage(playerData, true) { To = client.Value });
                            }
                        }
                    }
                    if (!string.IsNullOrEmpty(subsystemGameInfo.WorldSettings.BlocksTextureName)) {
                        using var s = Storage.OpenFile(BlocksTexturesManager.GetFileName(subsystemGameInfo.WorldSettings.BlocksTextureName), OpenFileMode.Read);
                        textureData = ModsManager.StreamToBytes(s);
                    }
                    byte[] data = NetworkManager.GetNowProject(GameManager.Project, c);
                    NetworkManager.QueuePackage(new ProjectPackage(textureData, data) { To = c });
                    break;
                }
            case ClientStage.NotConnected:
                subsystemPlayers.MakePlayerOffline(c.PlayerGuid);
                break;
            case ClientStage.ProjectLoaded: {
                    List<Entity> sendList = [.. GameManager.Project.Entities];
                    NetworkManager.QueuePackage(new EntityPackage(sendList) { To = c });
                    if (c.CachePlayerEntity != null) {
                        GameManager.Project.AddEntity(c.CachePlayerEntity);
                        c.CachePlayerEntity = null;
                    }
                    break;
                }
            case ClientStage.LoadTerrain:
                if (c.PlayerData?.ComponentPlayer != null
                    && subsystemGameInfo.WorldSettings.GameMode != GameMode.Creative) {
                    c.PlayerData.ComponentPlayer.ComponentHealth.IsInvulnerable = true;
                }
                break;
            case ClientStage.Playing:
                if (c.PlayerData == null) {
                    RemoveClient(c, "错误:该Client没有对应PlayerData");
                }
                else if (c.PlayerData.ComponentPlayer != null
                    && subsystemGameInfo.WorldSettings.GameMode != GameMode.Creative) {
                    c.PlayerData.ComponentPlayer.ComponentHealth.IsInvulnerable = false;
                }
                break;
        }
    }

    public void ClearNetworkReceiveEvent() {
        NetworkReceiveEvent = null;
    }

    public void ClearPeerConnectedEvent() {
        PeerConnectedEvent = null;
    }

    public void ClearPeerDisconnectedEvent() {
        PeerDisconnectedEvent = null;
    }

    public void ClearNetworkReceiveUnconnectedEvent() {
        NetworkReceiveUnconnectedEvent = null;
    }

    public void ClearNetworkErrorEvent() {
        NetworkErrorEvent = null;
    }

    public void ClearConnectionRequestEvent() {
        ConnectionRequestEvent = null;
    }

    public void ClearDeliveryEvent() {
        DeliveryEvent = null;
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
