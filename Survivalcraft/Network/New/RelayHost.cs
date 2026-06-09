using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Engine;
using LiteNetLib;
using LiteNetLib.Utils;

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
        _packageFragment = new PacketFragment();

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

    public void Board(Packet packet) => 
        Send(packet, [.. _clientSessions.Keys]);

    public void Send(Packet packet, int playerIndex) => 
        Send(packet, [playerIndex]);

    void Send(Packet packet, List<int> playerIndexes) {
        List<int> indexs = [];
        playerIndexes.ForEach((index) => {
            if (_clientSessions.ContainsKey(index)) {
                indexs.Add(index);
            }
        });

        PacketStreamWriter writer = new();
        writer.Write(PacketFragment.RelayVerifyByte);
        writer.Write(packet.ID);
        writer.Write(indexs.Count);
        indexs.ForEach(writer.Write);
        packet.Serialize(new PacketSerializer(writer));

        foreach (NetDataWriter chunk in PacketFragment.FragmentPayload(writer.Data())) {
            _relayPeer?.Send(chunk, DeliveryMethod.ReliableOrdered);
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
        if (_packageFragment.TryProcessIncoming(reader, peer.Address.ToString(), out byte[] payload)) {
            PacketManager.DecodePackets(payload).ForEach((packet) => {
                ClientSession clientSession = _clientSessions.FirstOrDefault(
                    session => session.Value.PlayerIndex == packet.SenderPlayerIndex).Value;
                packet.From = clientSession;
                packet.SenderPlayerIndex = packet.SenderPlayerIndex;
                packet.Handle(true);
            });
        }
    }

    public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) {
        Log.Information($"Unconnected receive by: {remoteEndPoint}");
    }

    public void OnPeerConnected(NetPeer peer) {
        _relayPeer = peer;
        Log.Information($"Relay connected: {peer.Address}");
    }

    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo) {
        _relayPeer = null;
        Log.Information($"Relay disconnected: {peer.Address}");
    }
}
