using System.Net;
using System.Net.Sockets;
using Engine;
using Game.Network;
using GameEntitySystem;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Game.Network;

public class NetworkServer : INetEventListener {
    public event Action<NetPeer> ClientConnected;
    public event Action<NetPeer, DisconnectInfo> ClientDisconnected;

    public readonly NetManager _server;
    private readonly PacketFragment m_chunkTransport;

    public Client Self { get; set; }
    public Client Server { get; set; }
    public NetworkStage CurrentStage { get; set; } = NetworkStage.NotConnected;
    public bool IsServer => CurrentStage == NetworkStage.Connected && Self == Server;
    public int ClientCount => Peers.Count() + 1;
    public Dictionary<int, Client> Clients { get; } = new Dictionary<int, Client>();
    public IEnumerable<Client> Peers => Clients.Values.Where(c => c != Self);
    public List<Packet> PendingPackets { get; } = new List<Packet>();
    public List<Packet> PendingHandlePackets { get; } = new List<Packet>();
    public Action<Client> OnClientStateChanged { get; set; }
    public Guid TokenId { get; set; }
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
        m_chunkTransport = new PacketFragment(_server);
    }

    public bool StartServer(int port) {
        if (_server.IsRunning) {
            Stop();
        }
        try {
            StartLocal();
            if (_server.Start(port)) {
                Log.Information($"Server started successfully on port {_server.LocalPort}");
                Window.Frame += Update;
                Window.Closed += Stop;
                _server.PollEvents();
                CurrentStage = NetworkStage.Connected;
                Server = Self;
                WorkType = WorkType.Server;
                OnClientStateChanged += ClientStateChanged;
                Project.EntityAdded += (_, arg) => {
                    NetworkManager.QueuePacket(new EntityLoadPacket(arg.Entity));
                };
                Project.EntityRemoved += (_, arg) => {
                    NetworkManager.QueuePacket(new EntityRemovePacket(arg.Entity.Id));
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
        var c = new Client(peer, FindUnusedIndex(), tokenId, guid, nickname);
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
                NetworkManager.QueuePacket(new ClientRemovePacket(c.ID));
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

    public void SendWriterFromPacket(Packet packet, IPEndPoint endPoint, bool isSend = true) {
        NetworkHelper.SendWriterFromPacket(this, m_chunkTransport, packet, endPoint, isSend);
    }

    public void SendWriterFromPacket(Packet packet, NetPeer netPeer, bool useDeliveryEvent = false) {
        NetworkHelper.SendWriterFromPacket(this, m_chunkTransport, packet, netPeer, useDeliveryEvent);
    }

    public void SendWriterFromPacket(Packet packet, ConnectionRequest request, bool reject) {
        NetworkHelper.SendWriterFromPacket(this, m_chunkTransport, packet, request, reject);
    }

    public void SendWriterFromPackets(IEnumerable<Packet> packets, NetPeer netPeer = null, ConnectionRequest request = null, IPEndPoint iPEndPoint = null, bool useDeliveryEvent = false) {
        NetworkHelper.SendWriterFromPackets(this, m_chunkTransport, packets, netPeer, request, iPEndPoint, useDeliveryEvent);
    }

    public void Stop() {
        if (NetManager.IsRunning) {
            RemoveAllClients("Server is shutting down.");
            NetManager.Stop(true);
        }
    }

    public void Update() {
        if (NetManager.IsRunning) {
            foreach (Client client in Clients.Values) {
                if (!client.IsConnected) {
                    continue;
                }
                byte[] payload = NetworkHelper.BuildPayloadForClient(PendingPackets, client, false, IsServer);
                SendPayload(payload, client.Peer);
            }
            PendingPackets.Clear();
            m_chunkTransport.CleanupStates();
            NetManager.PollEvents();
        }
    }

    public byte[] BuildOutgoingPayload(IEnumerable<Packet> packets, bool isServer) {
        return NetworkHelper.BuildOutgoingPayload(packets);
    }

    public void SendPayload(byte[] payload, NetPeer netPeer = null, ConnectionRequest request = null, IPEndPoint iPEndPoint = null, bool useDeliveryEvent = false) {
        m_chunkTransport.SendPayload(payload, netPeer, request, iPEndPoint, useDeliveryEvent);
    }

    public void CleanupChunkStates() {
        m_chunkTransport.CleanupStates();
    }

    public bool TryDecodePackets(NetDataReader reader, NetPeer peer, ConnectionRequest request, IPEndPoint endPoint, out List<Packet> packets) {
        return NetworkHelper.TryDecodePackets(Clients.Values, m_chunkTransport, reader, peer, request, endPoint, out packets);
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

        NetworkManager.QueuePacket(new ClientStateChangePacket(c.ID, c.State) { Except = c });

        switch (c.State) {
            case ClientStage.Connected: {
                    byte[] textureData = null;
                    if (subsystemPlayers.MakePlayerOnline(c.PlayerGuid, out PlayerData playerData, out Entity entity)) {
                        if (entity != null) {
                            // Keep restored players on the main world path immediately so
                            // concurrent joiners cannot miss them in later entity snapshots.
                            GameManager.Project.AddEntity(entity);
                        }
                        NetworkManager.QueuePacket(new PlayerAddPacket(playerData) { Except = c });
                    }
                    if (!string.IsNullOrEmpty(subsystemGameInfo.WorldSettings.BlocksTextureName)) {
                        using var s = Storage.OpenFile(BlocksTexturesManager.GetFileName(subsystemGameInfo.WorldSettings.BlocksTextureName), OpenFileMode.Read);
                        textureData = ModsManager.StreamToBytes(s);
                    }
                    byte[] data = NetworkManager.GetNowProject(GameManager.Project);
                    NetworkManager.QueuePacket(new ProjectPacket(textureData, data) { To = c });
                    break;
                }
            case ClientStage.NotConnected:
                subsystemPlayers.MakePlayerOffline(c.PlayerGuid);
                break;
            case ClientStage.ProjectLoaded: {
                    List<Entity> sendList = [.. GameManager.Project.Entities];
                    NetworkManager.QueuePacket(new EntityLoadPacket(sendList) { To = c });
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
        NetworkHelper.HandleNetworkReceive(this, m_chunkTransport, peer, dataReader);
    }

    void INetEventListener.OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) {
        NetworkHelper.HandleUnconnectedEvent(Clients.Values, IsServer, m_chunkTransport, remoteEndPoint, reader, false);
    }

    void INetEventListener.OnNetworkError(IPEndPoint endPoint, SocketError socketError) {
    }

    void INetEventListener.OnConnectionRequest(ConnectionRequest request) {
        Log.Information("Received connection request " + request.RemoteEndPoint);

        if (GameManager.Project == null) {
            SendWriterFromPacket(new ConnectionRejectPacket("Please wait until server startup is complete before joining."), request, true);
        }
        else {
            try {
                ConnectionRequestPacket requestPacket = PacketManager.DecodePacket<ConnectionRequestPacket>(Clients.Values, request.Data, null, request);
                requestPacket.Handle(true);
            }
            catch (Exception e) {
                SendWriterFromPacket(new ConnectionRejectPacket($"Invalid connection request packet: {e.Message}"), request, true);
                Log.Information("ConnectionRequestEvent Error:" + e.StackTrace);
            }
        }
    }

    void INetEventListener.OnNetworkLatencyUpdate(NetPeer peer, int latency) {
    }
}


