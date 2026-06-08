using System;
using System.Net;
using System.Net.Sockets;
using Engine;
using LiteNetLib;

namespace Game.Network;

public class NetworkClient : INetEventListener {
    private NetPeer _serverPeer;
    private NetManager _netManager;
    private IPEndPoint _serverAddress;
    private PacketFragment _packageFragment;

    public bool Connect(IPEndPoint serverAddress) {
        _netManager = new NetManager(this) {
            MaxConnectAttempts = 6,
            DisconnectTimeout = 10000,
            UnconnectedMessagesEnabled = true,
            UpdateTime = 25,
            ReuseAddress = true
        };
        _netManager.Start();
        _packageFragment = new PacketFragment(_netManager);

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

        _packageFragment.Send(writer.Data(), _serverPeer);
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
        _packageFragment.TryRead(reader, peer.Address.ToString()).ForEach(packet => {
            packet.Handle(false);
        });
    }


    public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) {
        _packageFragment.TryRead(reader, remoteEndPoint.ToString()).ForEach(packet => {
            packet.Handle(false);
        });
        Log.Information($"Unconnected receive by: {remoteEndPoint}");
    }

    public void OnPeerConnected(NetPeer peer) {
        _serverPeer = peer;
        Log.Information($"Connected: {_serverAddress}");
    }

    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo) {
        _serverPeer = null;
        Log.Information($"Disconnected: {_serverAddress}");
    }
}
