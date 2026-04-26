using System.Net;
using System.Net.Sockets;
using Engine;
using Game.NetWork.Packages;
using GameEntitySystem;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Game.NetWork;

public class NetworkServer : INetworkNode, INetEventListener {
    public event Action<NetPeer> ClientConnected;
    public event Action<NetPeer, DisconnectInfo> ClientDisconnected;

    public readonly NetManager _server;
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

    public NetManager NetManager => _server;

    internal readonly Dictionary<Client, string> ClientsToRemove = new Dictionary<Client, string>();

    public NetworkServer() {
        _server = new NetManager(this) {
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
        if (_server.IsRunning) {
            StopImmediate();
        }
        try {
            isStopping = false;
            StartLocal();
            if (_server.Start(port)) {
                Log.Information($"Server started successfully on port {_server.LocalPort}");
                Window.Frame += Update;
                Window.Closed += StopImmediate;
                _server.PollEvents();
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
                        if (entity != null) {
                            // Keep restored players on the main world path immediately so
                            // concurrent joiners cannot miss them in later entity snapshots.
                            GameManager.Project.AddEntity(entity);
                        }
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

    void INetEventListener.OnPeerConnected(NetPeer peer) {
        ClientConnected?.Invoke(peer);
    }

    void INetEventListener.OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo) {
        ClientDisconnected?.Invoke(peer, disconnectInfo);
    }

    void INetEventListener.OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod) {
        var dataReader = new NetDataReader(reader.RawData, reader.Position, reader.AvailableBytes);
        m_packetTransport.HandleNetworkReceive(peer, dataReader);
    }

    void INetEventListener.OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) {
        m_packetTransport.HandleUnconnectedEvent(remoteEndPoint, reader, false);
    }

    void INetEventListener.OnNetworkError(IPEndPoint endPoint, SocketError socketError) {
    }

    void INetEventListener.OnConnectionRequest(ConnectionRequest request) {
        Log.Information("Received connection request " + request.RemoteEndPoint);

        if (GameManager.Project == null) {
            SendWriterFromPackage(new ConnectionRejectPackage("Please wait until server startup is complete before joining."), request, true);
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

    void INetEventListener.OnNetworkLatencyUpdate(NetPeer peer, int latency) {
    }
}
