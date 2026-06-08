using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Engine;
using LiteNetLib;

namespace Game.Network;

public class RelayHost : INetEventListener {
    private NetPeer _relayPeer;
    private NetManager _netManager;
    private IPEndPoint _relayAddress;
    private PacketFragment _packageFragment;
    private Dictionary<int, ClientSession> _clientSessions = new();

    public bool Start(IPEndPoint relayAddress) {
        _netManager = new NetManager(this) {
            MaxConnectAttempts = 6,
            DisconnectTimeout = 10000,
            UnconnectedMessagesEnabled = true,
            UpdateTime = 25,
            ReuseAddress = true
        };
        _netManager.Start();
        _packageFragment = new PacketFragment(_netManager);

        _relayAddress = relayAddress;
        _netManager.Connect(_relayAddress, "x26.06.01");

        Log.Information($"Connecting (Relay) {_relayAddress}");

        return _netManager.IsRunning;
    }

    public void Disconnect() {
        _netManager.Stop();
        _relayPeer = null;
        Log.Information($"Disconnecting {_relayAddress}");
    }

    public void Update() {
        _netManager.PollEvents();
        _packageFragment.CleanupStates();
    }

    public void Board(Packet packet) {
        List<int> playerIndexs = [];
        PacketStreamWriter writer = new();

        foreach (KeyValuePair<int, ClientSession> clientSession in _clientSessions) {
            if (packet.MinNeedState <= clientSession.Value.NetworkState) {
                playerIndexs.Add(clientSession.Key);
            }
        }
        writer.Write(PacketFragment.VerifyByte);
        writer.Write(packet.ID);
        writer.Write(playerIndexs.Count);
        foreach (int index in playerIndexs) {
            writer.Write(index);
        }
        packet.Serialize(new PacketSerializer(writer));

        _packageFragment.Send(writer.Data(), _relayPeer);
    }

    public void Send(Packet packet, int playerIndex) {
        KeyValuePair<int, ClientSession> clientSession =
            _clientSessions.FirstOrDefault(kvp => kvp.Value.PlayerIndex == playerIndex);

        if (packet.MinNeedState <= clientSession.Value.NetworkState) {
            PacketStreamWriter writer = new();
            writer.Write(PacketFragment.VerifyByte);
            writer.Write(packet.ID);
            writer.Write(1);
            writer.Write(playerIndex);
            packet.Serialize(new PacketSerializer(writer));

            _packageFragment.Send(writer.Data(), _relayPeer);
        }
    }

    public void OnConnectionRequest(ConnectionRequest request) {
        Log.Information($"Connection request by: {request.RemoteEndPoint}");
    }

    public void OnNetworkError(IPEndPoint endPoint, SocketError socketError) {
        Log.Error($"Network error on {endPoint}: {socketError}");
    }

    public void OnNetworkLatencyUpdate(NetPeer peer, int latency) {
        // 可以用来计算延迟
    }

    public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod) {
        // 这里仍然需要权限检测
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

    // 无法按照peer索引玩家
    public void OnPeerConnected(NetPeer peer) {
        Log.Information($"Relay connected: {peer.Address}");
    }

    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo) {
        Log.Information($"Relay disconnected: {peer.Address}");
    }
}
