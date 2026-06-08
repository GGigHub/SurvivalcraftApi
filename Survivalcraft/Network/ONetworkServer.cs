using System.Net;
using System.Net.Sockets;
using Engine;
using GameEntitySystem;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Game.Network;

public class ONetworkServer : INetEventListener {
    public event Action<NetPeer> ClientConnected;
    public event Action<NetPeer, DisconnectInfo> ClientDisconnected;

    public ClientSession Server { get; set; }
    public bool IsServer => NetManager.IsRunning && Self == Server;
    public int ClientCount => Peers.Count() + 1;
    public Dictionary<int, ClientSession> Clients { get; } = new Dictionary<int, ClientSession>();
    public Dictionary<int, NetPeer> ClientPeers { get; } = new Dictionary<int, NetPeer>();
    public IEnumerable<ClientSession> Peers => Clients.Values.Where(c => c != Self);
    public List<Packet> PendingPackets { get; } = new List<Packet>();
    public Action<ClientSession> OnNetworkStateChanged { get; set; }

    internal readonly Dictionary<ClientSession, string> ClientsToRemove = new Dictionary<ClientSession, string>();

    public ClientSession Self { get; set; }
    public NetManager NetManager { get; set; }
    public PacketFragment PackageFragment { get; set; }

    public ONetworkServer() {
        NetManager = new NetManager(this) {
            MaxConnectAttempts = 6,
            DisconnectTimeout = 10000,
            UnconnectedMessagesEnabled = true,
            UpdateTime = 25,
            ReuseAddress = true
        };
        PackageFragment = new PacketFragment();
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

    public void AddClient(ClientSession c) {
        Clients[c.PlayerIndex] = c;
    }

    public void RemoveClientImmediate(ClientSession c, string reason = null) {
        if (c == Self) {
            Stop();
            return;
        }

        if (ClientPeers.TryGetValue(c.PlayerIndex, out NetPeer peer) && peer.ConnectionState != ConnectionState.Disconnected) {
            if (string.IsNullOrEmpty(reason)) peer.Disconnect();
            else peer.Disconnect(NetDataWriter.FromString(reason));
        }

        if (c == Server) {
            Stop();
            return;
        }

        if (Clients.ContainsKey(c.PlayerIndex)) {
            //NetworkService.QueuePacket(new ClientRemovePacket(c.PlayerIndex));
            c.NetworkState = NetworkState.NotConnected;
            OnNetworkStateChanged?.Invoke(c);
            Clients.Remove(c.PlayerIndex);
            ClientPeers.Remove(c.PlayerIndex);
        }
    }

    public void RemoveAllClients(string reason = null) {
        var cs = new List<ClientSession>(Clients.Values);
        foreach (var c in cs) {
            if (c == Self) continue;
            if (ClientPeers.TryGetValue(c.PlayerIndex, out NetPeer peer) && peer.ConnectionState != ConnectionState.Disconnected) {
                if (reason != null) peer.Disconnect(NetDataWriter.FromString(reason));
                else peer.Disconnect();
            }
            Clients.Remove(c.PlayerIndex);
            ClientPeers.Remove(c.PlayerIndex);
            c.NetworkState = NetworkState.NotConnected;
        }
    }

    public ClientSession GetClientByPlayerIndex(int playerIndex) {
        if (Clients.ContainsKey(playerIndex))
            return Clients[playerIndex];
        return null;
    }

    public ClientSession GetClientByGUID(Guid? guid) {
        return Clients.Values.FirstOrDefault(c => c.NetworkGuid == guid);
    }

    public void StartLocal() {
        Clients.Clear();
        Self = new ClientSession { NetworkGuid = new Guid(SettingsManager.OnlineAccessToken) };
        AddClient(Self);
    }

    public ClientSession CreateClient(NetPeer peer, Guid guid, string dataId) {
        if (!IsServer) throw new InvalidOperationException("creating client as non-server");
        foreach (var p in Peers) {
            if (p.NetworkGuid == guid) RemoveClient(p, "Duplicate account login.");
        }
        var c = new ClientSession { PlayerIndex = FindUnusedIndex(), NetworkGuid = guid };
        ClientPeers[c.PlayerIndex] = peer;
        peer.Tag = c;
        return c;
    }

    public void RemoveClient(ClientSession c, string reason = null) {
        if (ClientsToRemove.ContainsKey(c)) {
            if (!string.IsNullOrEmpty(reason))
                ClientsToRemove[c] += '\n' + reason;
        }
        else {
            ClientsToRemove[c] = reason ?? string.Empty;
        }
    }

    public void SendWriterFromPacket(Packet packet, NetPeer netPeer, bool useDeliveryEvent = false) {
        NetworkHelper.SendWriterFromPacket(NetManager, packet, netPeer, useDeliveryEvent);
    }

    public void SendWriterFromPacket(Packet packet, ConnectionRequest request, bool reject) {
        NetworkHelper.SendWriterFromPacket(NetManager, packet, request, reject);
    }

    public void Stop() {
        if (NetManager.IsRunning) {
            RemoveAllClients("Server is shutting down.");
            NetManager.Stop(true);
        }
    }

    public void Update() {
        if (NetManager.IsRunning) {
            foreach (ClientSession client in Clients.Values) {
                    if (client == Self || client.NetworkState == NetworkState.NotConnected) {
                    continue;
                }
                byte[] payload = NetworkHelper.BuildPayloadForClient(PendingPackets, client, true, IsServer);
                if (payload != null && ClientPeers.TryGetValue(client.PlayerIndex, out NetPeer peer)) {
                    foreach (var chunk in PacketFragment.FragmentPayload(payload)) {
                        peer.Send(chunk, DeliveryMethod.ReliableOrdered);
                    }
                }
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

    private int FindUnusedIndex() {
        for (int i = 1; i < int.MaxValue; i++) {
            if (!Clients.ContainsKey(i)) return i;
        }
        throw new InvalidOperationException("Too many clients.");
    }

    public void OnPeerConnected(NetPeer peer) {
        ClientConnected?.Invoke(peer);
    }

    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo) {
        ClientDisconnected?.Invoke(peer, disconnectInfo);
        ClientSession client = peer.Tag as ClientSession;
        if (client != null) {
            RemoveClient(client, disconnectInfo.Reason.ToString());
        }
        GameManager.Project.FindSubsystem<SubsystemPlayers>().MakePlayerOffline(client?.NetworkGuid ?? Guid.Empty);
        Log.Information("Disconnect by " + disconnectInfo);
    }

    public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod) {
        if (GameManager.Project == null) return;

        if (!NetworkHelper.TryDecodePackets(Clients.Values, 
            PackageFragment, reader, peer, null, null, out List<Packet> packets)) {
            return;
        }
        foreach (var packet in packets) {
            if (packet.Connection == null) {
                continue;
            }
            if (packet.MinNeedState <= packet.Connection.NetworkState) {
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
            //SendWriterFromPacket(new ConnectionRejectPacket("Please wait until server startup is complete before joining."), request, true);
        }
        else {
            try {
                //ConnectionRequestPacket requestPacket = PacketManager.DecodePacket<ConnectionRequestPacket>(Clients.Values, request.Data, null, request);
                //requestPacket.Handle(true);
            }
            catch (Exception e) {
                //SendWriterFromPacket(new ConnectionRejectPacket($"Invalid connection request packet: {e.Message}"), request, true);
                Log.Information("ConnectionRequestEvent Error:" + e.StackTrace);
            }
        }
    }

    public void OnNetworkLatencyUpdate(NetPeer peer, int latency) {
    }

    internal PlayerData GetPlayerByClient(ClientSession client) {
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
