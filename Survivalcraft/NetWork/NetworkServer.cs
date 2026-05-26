using System.Net;
using System.Net.Sockets;
using Engine;
using GameEntitySystem;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Game.Network;

public class NetworkServer : INetEventListener {
    public event Action<NetPeer> ClientConnected;
    public event Action<NetPeer, DisconnectInfo> ClientDisconnected;

    public Client Server { get; set; }
    public bool IsServer => NetManager.IsRunning && Self == Server;
    public int ClientCount => Peers.Count() + 1;
    public Dictionary<int, Client> Clients { get; } = new Dictionary<int, Client>();
    public IEnumerable<Client> Peers => Clients.Values.Where(c => c != Self);
    public List<Packet> PendingPackets { get; } = new List<Packet>();
    public Action<Client> OnNetworkStateChanged { get; set; }

    internal readonly Dictionary<Client, string> ClientsToRemove = new Dictionary<Client, string>();

    public Client Self { get; set; }
    public NetManager NetManager { get; set; }
    public PacketFragment PackageFragment { get; set; }

    public NetworkServer() {
        NetManager = new NetManager(this) {
            MaxConnectAttempts = 6,
            DisconnectTimeout = 10000,
            UnconnectedMessagesEnabled = true,
            UpdateTime = 25,
            ReuseAddress = true
        };
        PackageFragment = new PacketFragment(NetManager);
    }

    public bool StartServer(int port) {
        if (NetManager.IsRunning) {
            Stop();
        }
        try {
            StartLocal();
            if (NetManager.Start(port)) {
                Log.Information($"Server started successfully on port {NetManager.LocalPort}");
                NetManager.PollEvents();
                Server = Self;
                Project.EntityAdded += (_, arg) => {
                    NetworkService.QueuePacket(new EntityLoadPacket(arg.Entity));
                };
                Project.EntityRemoved += (_, arg) => {
                    NetworkService.QueuePacket(new EntityRemovePacket(arg.Entity.Id));
                };
            }
            else {
                Stop();
            }
        }
        catch (Exception e) {
            Log.Error(e.Message);
        }
        return NetManager.IsRunning;
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
            return;
        }

        if (Clients.ContainsKey(c.ID)) {
            NetworkService.QueuePacket(new ClientRemovePacket(c.ID));
            c.State = NetworkState.NotConnected;
            OnNetworkStateChanged?.Invoke(c);
            Clients.Remove(c.ID);
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
            c.State = NetworkState.NotConnected;
        }
    }

    public Client GetClientByID(byte id) {
        if (Clients.ContainsKey(id))
            return Clients[id];
        return null;
    }

    public Client GetClientByGUID(Guid? guid) {
        return Clients.Values.FirstOrDefault(c => c.NetworkGuid == guid);
    }

    public void StartLocal() {
        Clients.Clear();
        Self = new Client(new Guid(SettingsManager.OnlineAccessToken));
        AddClient(Self);
    }

    public Client CreateClient(NetPeer peer, Guid guid, string dataId, string nickname) {
        if (!IsServer) throw new InvalidOperationException("creating client as non-server");
        foreach (var p in Peers) {
            if (p.NetworkGuid == guid) RemoveClient(p, "Duplicate account login.");
        }
        var c = new Client(peer, FindUnusedIndex(), guid, nickname);
        peer.Tag = c;
        return c;
    }

    public void RemoveClient(Client c, string reason = null) {
        if (ClientsToRemove.ContainsKey(c)) {
            if (!string.IsNullOrEmpty(reason))
                ClientsToRemove[c] += '\n' + reason;
        }
        else {
            ClientsToRemove[c] = reason ?? string.Empty;
        }
    }

    public void SendWriterFromPacket(Packet packet, NetPeer netPeer, bool useDeliveryEvent = false) {
        NetworkHelper.SendWriterFromPacket(PackageFragment, packet, netPeer, useDeliveryEvent);
    }

    public void SendWriterFromPacket(Packet packet, ConnectionRequest request, bool reject) {
        NetworkHelper.SendWriterFromPacket(PackageFragment, packet, request, reject);
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
                if (client == Self || !client.IsConnected) {
                    continue;
                }
                byte[] payload = NetworkHelper.BuildPayloadForClient(PendingPackets, client, true, IsServer);
                if (payload != null) PackageFragment.SendPayload(payload, client.Peer);
            }
            PendingPackets.Clear();

            // Handle deferred client removal
            if (ClientsToRemove.Count > 0) {
                foreach (var pair in ClientsToRemove) {
                    RemoveClientImmediate(pair.Key, pair.Value);
                }
                ClientsToRemove.Clear();
            }

            PackageFragment.CleanupStates();
            NetManager.PollEvents();
        }
    }

    private byte FindUnusedIndex() {
        for (byte i = 0; i < 255; i++) {
            if (!Clients.ContainsKey(i)) return i;
        }
        throw new Exception("Server is full.");
    }

    public void OnPeerConnected(NetPeer peer) {
        ClientConnected?.Invoke(peer);
    }

    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo) {
        ClientDisconnected?.Invoke(peer, disconnectInfo);
        Client client = Clients.Values.FirstOrDefault(c => c.Peer == peer);
        if (client != null) {
            RemoveClient(client, disconnectInfo.Reason.ToString());
        }
        GameManager.Project.FindSubsystem<SubsystemPlayers>().MakePlayerOffline(client.NetworkGuid);
        Log.Information("Disconnect by " + disconnectInfo);
    }

    public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod) {
        if (GameManager.Project == null) return;

        if (!NetworkHelper.TryDecodePackets(Clients.Values, 
            PackageFragment, reader, peer, null, null, out List<Packet> packets)) {
            return;
        }
        foreach (var packet in packets) {
            if (packet.From == null) {
                continue;
            }
            if (packet.MinNeedState <= packet.From.State) {
                packet.Handle(true);
            }
        }
    }

    public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) {
        NetworkHelper.HandleUnconnectedEvent(Clients.Values, IsServer, PackageFragment, remoteEndPoint, reader, false);
    }

    public void OnNetworkError(IPEndPoint endPoint, SocketError socketError) {
    }

    public void OnConnectionRequest(ConnectionRequest request) {
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
