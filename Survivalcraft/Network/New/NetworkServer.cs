using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using Engine;
using LiteNetLib;
using LiteNetLib.Utils;
namespace Game.Network;

public class NetworkServer : INetEventListener {
    private NetManager _netManager;
    private PacketFragment _packageFragment;

    public bool IsRunning => _netManager != null && _netManager.IsRunning;
    public Dictionary<NetPeer, ClientSession> ClientSessions { get; set; } = [];

    public bool Start() {
        _netManager = new NetManager(this) {
            MaxConnectAttempts = 6,
            DisconnectTimeout = 10000,
            UnconnectedMessagesEnabled = true,
            UpdateTime = 25,
            ReuseAddress = true
        };
        _packageFragment = new PacketFragment();

        _netManager.Start(SettingsManager.ServerPort);

        Log.Information("Server started");
        return _netManager.IsRunning;
    }

    public void Stop() {
        _netManager.Stop(true);
        ClientSessions.Clear();
    }

    public void Update() {
        _netManager.PollEvents();
        _packageFragment.CleanupStates();
    }

    public void Board(Packet packet) {
        if (packet.To != null) {
            Send(packet, packet.To);
            return;
        }
        foreach (KeyValuePair<NetPeer, ClientSession> clientSession in ClientSessions) {
            if (clientSession.Value != packet.Except) {
                Send(packet, clientSession.Value);
            }
        }
    }

    public void Send(Packet packet, ClientSession session) {
        if (packet.MinNeedState <= session.NetworkState) {
            PacketStreamWriter writer = new();
            writer.Write(PacketFragment.VerifyByte);
            writer.Write(packet.ID);
            packet.Serialize(new PacketSerializer(writer));

            NetPeer peer = ClientSessions.FirstOrDefault(kvp => kvp.Value == session).Key;
            foreach (NetDataWriter chunk in PacketFragment.FragmentPayload(writer.Data())) {
                peer.Send(chunk, DeliveryMethod.ReliableOrdered);
            }
        }
    }

    public void OnConnectionRequest(ConnectionRequest request) {
        Log.Information($"Connection request by: {request.RemoteEndPoint}");
        request.AcceptIfKey("x26.06.01");
    }

    public void OnNetworkError(IPEndPoint endPoint, SocketError socketError) {
        Log.Error($"Network error on {endPoint}: {socketError}");
    }

    public void OnNetworkLatencyUpdate(NetPeer peer, int latency) {
        // 可以用来计算延迟
    }

    public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod) {
        if (_packageFragment.TryProcessIncoming(reader, peer.Address.ToString(), out byte[] payload)) {
            PacketManager.DecodePackets(payload).ForEach((packet) => {
                if (!ClientSessions.TryGetValue(peer, out ClientSession session)) return;
                if (packet.MinNeedState <= session.NetworkState) {
                    packet.From = session;
                    packet.Handle(true);
                }
            });
        }
    }

    public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) {
        Log.Information($"Unconnected receive by: {remoteEndPoint}");
    }

    public void OnPeerConnected(NetPeer peer) {
        ClientSession session = new() {
            PlayerIndex = -1,
            NetworkGuid = Guid.Empty,
            NetworkState = NetworkState.Connected
        };
        ClientSessions[peer] = session;

        Log.Information($"Connected: {peer.Address}");
    }

    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo) {
        if (ClientSessions.TryGetValue(peer, out ClientSession session)) {
            GameManager.Project.FindSubsystem<SubsystemNetwork>().MakePlayerOffline(session);
        }

        ClientSessions.Remove(peer);
        session.NetworkState = NetworkState.NotConnected;
        Log.Information($"Disconnected: {peer.Address}, {disconnectInfo}");
    }
}
