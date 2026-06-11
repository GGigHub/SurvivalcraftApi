using System;
using System.Net;
using System.Net.Sockets;
using Engine;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Game.Network;

public class ClientSession {
    // 不是ClientIndex
    public int PlayerIndex { get; set; }
    public Guid NetworkGuid { get; set; }
    public NetworkState NetworkState { get; set; }
}

public class NetworkClient : INetEventListener {
    private NetPeer _serverPeer;
    private NetManager _netManager;
    private IPEndPoint _serverAddress;
    private ClientSession _clientSession;
    private PacketFragment _packageFragment;

    public NetPeer ServerPeer => _serverPeer;
    public ClientSession MySession { get; set; }
    public bool IsConnected => _netManager != null && _netManager.IsRunning;

    public bool Connect(IPEndPoint serverAddress) {
        _netManager = new NetManager(this) {
            MaxConnectAttempts = 6,
            DisconnectTimeout = 10000,
            UnconnectedMessagesEnabled = true,
            UpdateTime = 25,
            ReuseAddress = true
        };
        _netManager.Start();
        _packageFragment = new PacketFragment();

        _serverAddress = serverAddress;
        _netManager.Connect(_serverAddress, "x26.06.01");

        Log.Information($"Connecting {_serverAddress}");
        return _netManager.IsRunning;
    }

    public void Disconnect() {
        _netManager.Stop();
        _serverPeer = null;
        Log.Information($"Disconnecting {_serverAddress}");
    }

    public void Update() {
        _netManager.PollEvents();
        _packageFragment.CleanupStates();
    }

    public void Send(Packet packet) {
        PacketStreamWriter writer = new();
        writer.Write(PacketFragment.VerifyByte);
        writer.Write(packet.ID);
        packet.Serialize(new PacketSerializer(writer));

        foreach (NetDataWriter chunk in PacketFragment.FragmentPayload(writer.Data())) {
            _serverPeer?.Send(chunk, DeliveryMethod.ReliableOrdered);
        }
    }

    public void OnConnectionRequest(ConnectionRequest request) {
        Log.Information($"Connection request by: {request.RemoteEndPoint}");
    }

    public void OnNetworkError(IPEndPoint endPoint, SocketError socketError) {
        Log.Error($"Network error on: {socketError}");
    }

    public void OnNetworkLatencyUpdate(NetPeer peer, int latency) {
        // 可以用来计算延迟
    }

    public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod) {
        if (_packageFragment.TryProcessIncoming(reader, peer.Address.ToString(), out byte[] payload)) {
            PacketManager.DecodePackets(payload).ForEach((packet) => {
                packet.Handle(false);
            });
        }
    }

    public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) {
        Log.Information($"Unconnected receive by: {remoteEndPoint}");
    }

    public void OnPeerConnected(NetPeer peer) {
        _serverPeer = peer;
        Log.Information($"Connected: {_serverAddress}");

        MySession = new ClientSession {
            PlayerIndex = -1,
            NetworkGuid = NetworkManager.NetworkGuid,
            NetworkState = NetworkState.NotConnected,
        };
        Send(new ConnectPacket(MySession.NetworkGuid));
    }

    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo) {
        _serverPeer = null;
        Log.Information($"Disconnected: {_serverAddress}");
    }
}
