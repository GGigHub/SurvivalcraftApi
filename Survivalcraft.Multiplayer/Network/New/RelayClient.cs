using System;
using System.Net;
using System.Net.Sockets;
using Engine;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Game.Network;

public sealed class RelayClient(string roomCode) : INetEventListener {
    private readonly string _roomCode = roomCode.Trim().ToUpperInvariant();
    private bool _disconnectByUser;
    private NetManager _netManager;
    private NetPeer _relayPeer;

    public ClientSession MySession { get; private set; }
    public bool IsConnected { get; private set; }
    public bool IsRunning => _netManager?.IsRunning == true;

    public void Connect(IPEndPoint relayAddress) {
        _netManager = new NetManager(this) {
            MaxConnectAttempts = 6,
            DisconnectTimeout = 10000,
            ChannelsCount = NetworkChannels.Count,
        };

        if (!_netManager.Start()) {
            throw new InvalidOperationException("Failed to start relay client transport.");
        }
        _netManager.Connect(relayAddress, RelayProtocol.ConnectionKey);
        Log.Information($"Connecting relay {relayAddress}, room={_roomCode}");
    }

    public void Update() {
        LatencyProbe.Update();
        _netManager.PollEvents();
    }

    public void Disconnect() {
        _disconnectByUser = true;
        _netManager.DisconnectAll();
        _netManager.PollEvents();
        _netManager.Stop();
    }

    public void Send(Packet packet) {
        PacketStreamWriter payload = new();
        payload.Write(PacketFragment.VerifyByte);
        payload.Write(packet.ID);
        packet.Serialize(new PacketSerializer(payload));
        foreach (NetDataWriter chunk in PacketFragment.FragmentPayload(payload.Data())) {
            NetDataWriter frame = RelayProtocol.Begin(RelayMessageType.DataToHost);
            frame.PutBytesWithLength(chunk.CopyData());
            _relayPeer?.Send(frame, packet.Channel, DeliveryMethod.ReliableOrdered);
        }
    }

    public void OnPeerConnected(NetPeer peer) {
        _relayPeer = peer;
        Log.Information($"Relay connected: {peer.Address}, joining room={_roomCode}");
        NetDataWriter writer = RelayProtocol.Begin(RelayMessageType.JoinRoom);
        writer.Put(_roomCode);
        peer.Send(writer, DeliveryMethod.ReliableOrdered);
    }

    public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod) {
        if (!RelayProtocol.TryReadHeader(reader, out RelayMessageType type)) return;
        if (type == RelayMessageType.JoinAccepted) {
            _ = reader.GetInt();
            Log.Information($"Relay room joined: {_roomCode}");
            MySession = new ClientSession {
                PlayerIndex = -1,
                NetworkGuid = Guid.Parse(SettingsManager.OnlineAccessToken),
                NetworkState = NetworkState.NotConnected
            };
            IsConnected = true;
            Send(new ConnectPacket(MySession.NetworkGuid));
        }
        else if (type == RelayMessageType.JoinRejected) {
            Log.Warning($"Relay room rejected: {_roomCode}");
            DialogsManager.Alert("Relay", reader.GetString());
            // 用户：不是我拒绝的
            _disconnectByUser = true;
            NetworkManager.Stop();
        }
        else if (type == RelayMessageType.DataToPeer) {
            byte[] data = reader.GetBytesWithLength();
            NetDataReader inner = new(data);
            if (PacketManager.PackageFragment.TryProcessIncoming(inner, "relay-host", out byte[] payload)) {
                foreach (Packet packet in PacketManager.DecodePackets(payload)) {
                    if (packet.MinNeedState < NetworkState.ProjectLoaded || GameManager.Project != null)
                        packet.Handle(false);
                }
            }
        }
    }

    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo) {
        IsConnected = false;
        MySession.NetworkState = NetworkState.NotConnected;

        LatencyProbe.Reset();
        Log.Information($"Relay disconnected: {peer.Address}, Reason: {disconnectInfo.Reason}");

        if (!_disconnectByUser) {
            GameManager.DisposeProject();
            ScreensManager.SwitchScreen("MainMenu");
            DialogsManager.Alert($"Relay disconnected: {peer.Address}", $"Reason: {disconnectInfo.Reason}");
        }
    }

    public void OnConnectionRequest(ConnectionRequest request) {
        Log.Information($"Connection request by: {request.RemoteEndPoint}");
    }

    public void OnNetworkError(IPEndPoint endPoint, SocketError socketError) {
        Log.Error($"Relay error on {endPoint}: {socketError}");
    }

    public void OnNetworkLatencyUpdate(NetPeer peer, int latency) {
        // 可以用来计算延迟
    }

    public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) {
        Log.Information($"Unconnected receive by: {remoteEndPoint}");
    }
}
