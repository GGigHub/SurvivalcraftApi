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
    private Dictionary<NetPeer, ClientSession> _clientSessions = [];

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
        _clientSessions.Clear();
    }

    public void Update() {
        _netManager.PollEvents();
        _packageFragment.CleanupStates();
    }

    public void Board(Packet packet) {
        foreach (KeyValuePair<NetPeer, ClientSession> clientSession in _clientSessions) {
            Send(packet, clientSession.Value.PlayerIndex);
        }
    }

    public void Send(Packet packet, int playerIndex) {
        KeyValuePair<NetPeer, ClientSession> clientSession =
            _clientSessions.FirstOrDefault(kvp => kvp.Value.PlayerIndex == playerIndex);

        if (packet.MinNeedState <= clientSession.Value.NetworkState) {
            PacketStreamWriter writer = new();
            writer.Write(PacketFragment.VerifyByte);
            writer.Write(packet.ID);
            packet.Serialize(new PacketSerializer(writer));

            foreach (NetDataWriter chunk in PacketFragment.FragmentPayload(writer.Data())) {
                clientSession.Key.Send(chunk, DeliveryMethod.ReliableOrdered);
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
                if (!_clientSessions.TryGetValue(peer, out ClientSession session)) return;
                packet.From = session;
                packet.SenderPlayerIndex = session.PlayerIndex;
                packet.Handle(false);
            });
        }
    }

    public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) {
        Log.Information($"Unconnected receive by: {remoteEndPoint}");
    }

    public void OnPeerConnected(NetPeer peer) {
        ClientSession session = new() {
            PlayerIndex = 0,
            NetworkGuid = Guid.Empty,
            NetworkState = NetworkState.Connected
        };
        _clientSessions[peer] = session;

        Log.Information($"Connected: {peer.Address}");
    }

    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo) {
        _clientSessions.Remove(peer);
        Log.Information($"Disconnected: {peer.Address}, {disconnectInfo}");
    }
}
