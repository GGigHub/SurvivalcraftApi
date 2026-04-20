using Engine;
using Game.NetWork.Packages;
using LiteNetLib;
using LiteNetLib.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace Game.NetWork;

public class NetworkBase {
    public enum Stage {
        NotConnected,
        WaitForClientList,
        Connected
    }

    public readonly EventBasedNetListener listener;
    public readonly NetManager netManager;
    private readonly NetworkFragment m_chunkTransport;
    private readonly NetPacketTransport m_packetTransport;
    private readonly NetRuntimeController m_runtimeController;

    public Client Self { get; set; }

    public Client Server { get; set; }

    public string Error { get; private set; }

    public Stage CurrentStage { get; set; } = Stage.NotConnected;

    public bool IsServer => CurrentStage == Stage.Connected && Self == Server;

    public int ClientCount => Peers.Count() + 1;

    public Dictionary<int, Client> Clients = new Dictionary<int, Client>();

    public NetPeer pendingPeer = null;

    public HashSet<byte> agreeOnPendingPeer = new HashSet<byte>();

    internal Client PendingClient => (Client)pendingPeer?.Tag;

    public IEnumerable<Client> Peers => Clients.Values.Where(c => c != Self);

    internal DateTime? pendingStartUtc;
    internal const double PendingJoinTimeoutSeconds = 2.0;
    internal readonly List<IPackage> PendingPackages = new List<IPackage>();
    internal readonly List<IPackage> PendingHandlePackages = new List<IPackage>();
    public event Action<IEnumerable<IPackage>> OnRecieve;
    public Action<Client> OnClientStateChanged;
    public Guid TokenId;
    internal readonly Dictionary<Client, string> ClientsToRemove = new Dictionary<Client, string>();
    internal bool isStopping;
    internal long sec_length;
    internal long sec_length_total;
    internal long sec_total;
    public bool AllowHandle = true;
    public WorkType WorkType { get; set; }

    public NetworkBase() {
        listener = new EventBasedNetListener();
        netManager = new NetManager(listener) {
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

    public void AddClient(Client c) {
        Clients[c.ID] = c;
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
            CurrentStage = Stage.NotConnected;
            return;
        }
        if (Clients.ContainsKey(c.ID)) {
            if (IsServer) {
                NetworkManager.QueuePackage(new ClientPackage(c.ID));
            }
            c.State = ClientState.NotConnected;
            OnClientStateChanged?.Invoke(c);
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
            c.State = ClientState.NotConnected;
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

    protected void PeerDisconnectedEvent(NetPeer peer, DisconnectInfo disconnectInfo) {
        m_packetTransport.HandlePeerDisconnected(peer, disconnectInfo);
    }

    public void AddPendingHandlePackage(IPackage package) {
        m_packetTransport.AddPendingHandlePackage(package);
    }

    public void NetworkReceiveEvent(NetPeer peer, NetDataReader reader, DeliveryMethod deliveryMethod) {
        m_packetTransport.HandleNetworkReceive(peer, reader);
    }

    public virtual void HandleConnectionReject(IPEndPoint sender, NetDataReader reader, UnconnectedMessageType type) {
    }

    public void TurnOnPackageHanlde(ProjectNet projectNet) {
        m_packetTransport.TurnOnPackageHandle(projectNet);
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

    public static int SendWriterFromPackage(NetManager netManager, IEnumerable<IPackage> packages, IPEndPoint endPoint) {
        return NetPacketTransport.SendWriterFromPackage(netManager, packages, endPoint);
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

    internal bool HasReceiveHandlers => OnRecieve != null;

    internal void DispatchReceivedPackages(IEnumerable<IPackage> packages) {
        OnRecieve?.Invoke(packages);
    }

    internal void ClearTransientHandlers() {
        OnRecieve = null;
        OnClientStateChanged = null;
    }

    internal void CleanupAfterStop() {
        Window.Closed -= StopImmediate;
        Window.Frame -= Update;
        listener.ClearConnectionRequestEvent();
        listener.ClearNetworkReceiveUnconnectedEvent();
        listener.ClearNetworkReceiveEvent();
        listener.ClearNetworkErrorEvent();
        listener.ClearPeerDisconnectedEvent();
        listener.ClearDeliveryEvent();
        listener.ClearPeerConnectedEvent();
        ClearTransientHandlers();
        CurrentStage = Stage.NotConnected;
        WorkType = WorkType.Local;
        if (Error != null) {
            DialogsManager.Confirm(Error, btn => {
                ScreensManager.SwitchScreen("MainMenu");
            });
        }
    }

    internal byte[] BuildOutgoingPayload(IEnumerable<IPackage> packages, bool isServer) {
        return m_packetTransport.BuildOutgoingPayload(packages, isServer);
    }

    internal void SendPayload(byte[] payload, NetPeer netPeer = null, ConnectionRequest request = null, IPEndPoint iPEndPoint = null, bool useDeliveryEvent = false) {
        m_chunkTransport.SendPayload(payload, netPeer, request, iPEndPoint, useDeliveryEvent);
    }

    internal void CleanupChunkStates() {
        m_chunkTransport.CleanupStates();
    }

    internal bool TryDecodePackages(NetDataReader reader, NetPeer peer, ConnectionRequest request, IPEndPoint endPoint, out List<IPackage> packages) {
        return m_packetTransport.TryDecodePackages(reader, peer, request, endPoint, out packages);
    }

    private byte FindUnusedIndex() {
        for (byte i = 0; i < 255; i++) {
            if (!Clients.ContainsKey(i)) return i;
        }
        throw new Exception("Server is full.");
    }

    public static bool IsValidIPAddress(string ip) {
        return IPAddress.TryParse(ip, out _);
    }

    public static bool IsValidPort(int port) {
        return port > 0 && port <= 65535;
    }
}
