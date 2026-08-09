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
    private readonly ClientPacketDispatcher _packetDispatcher = new();

    public ClientSession MySession { get; private set; }
    public bool IsConnected { get; private set; }
    public bool IsRunning => _netManager?.IsRunning == true;

    public void Connect(IPEndPoint relayAddress) {
        ApplicationReliabilityRuntime.ClearClient();
        _packetDispatcher.Reset();
        ContainerOpenResultTracker.Reset();
        FurnitureHammerResultTracker.Reset();
        CreativeFlyResultTracker.Reset();
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
        _packetDispatcher.Flush(MySession);
    }

    public void Disconnect() {
        _disconnectByUser = true;
        _netManager.DisconnectAll();
        _netManager.PollEvents();
        _netManager.Stop();
    }

    public void Send(Packet packet) {
        if (MySession != null) {
            if (MySession.CompatibilityState != NetworkCompatibilityState.Accepted
                && packet is not CompatibilityHelloPacket
                && packet is not ModChannelCatalogPacket) {
                return;
            }
            if (MySession.CompatibilityState == NetworkCompatibilityState.Accepted
                && MySession.AdmissionState != NetworkSessionAdmissionState.Accepted
                && packet is not ConnectPacket) {
                return;
            }
        }
        string logicalPacketType = packet.GetType().Name;
        Packet outbound = ApplicationReliabilityRuntime.ProtectClient(packet, MySession);
        if (outbound != null) {
            SendPrepared(outbound, logicalPacketType);
        }
    }

    internal bool SendPrepared(Packet packet, string logicalPacketType) {
        PacketStreamWriter payload = new();
        payload.Write(PacketFragment.VerifyByte);
        payload.Write(packet.ID);
        packet.Serialize(new PacketSerializer(payload));
        bool sent = false;
        foreach (NetDataWriter chunk in PacketFragment.FragmentPayload(payload.Data())) {
            NetDataWriter frame = RelayProtocol.Begin(RelayMessageType.DataToHost);
            frame.PutBytesWithLength(chunk.CopyData());
            if (_relayPeer != null) {
                NetPeer relayPeer = _relayPeer;
                NetworkFaultInjector.Send(
                    frame,
                    data => relayPeer.Send(data, packet.Channel, DeliveryMethod.ReliableOrdered),
                    logicalPacketType,
                    packet is ApplicationReliablePacket { Kind: ApplicationReliableFrameKind.Data });
                sent = true;
            }
        }
        return sent;
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
            ApplicationReliabilityRuntime.ClearClient();
            _packetDispatcher.Reset();
            ContainerOpenResultTracker.Reset();
            FurnitureHammerResultTracker.Reset();
            CreativeFlyResultTracker.Reset();
            _ = reader.GetInt();
            Log.Information($"Relay room joined: {_roomCode}");
            MySession = new ClientSession {
                TransportKind = "relay-client",
                PlayerIndex = -1,
                NetworkGuid = Guid.Parse(SettingsManager.OnlineAccessToken),
                NetworkState = NetworkState.NotConnected,
                CompatibilityState = NetworkCompatibilityState.AwaitingHello,
                CompatibilityStartedUtc = DateTime.UtcNow
            };
            IsConnected = true;
            NetworkSessionLifecycleTracker.RecordConnected(MySession);
            NetworkCompatibilityManifest manifest = NetworkCompatibilityRuntime.BeginClientHandshake(MySession);
            Send(new CompatibilityHelloPacket(manifest));
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
                    if (MySession?.CompatibilityState != NetworkCompatibilityState.Accepted) {
                        if (packet is not CompatibilityResultPacket
                            && packet is not ModChannelCatalogResultPacket) {
                            continue;
                        }
                    }
                    else if (MySession.AdmissionState != NetworkSessionAdmissionState.Accepted
                        && packet is not SessionAdmissionResultPacket) {
                        continue;
                    }
                    _packetDispatcher.Dispatch(MySession, packet);
                }
            }
        }
    }

    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo) {
        IsConnected = false;
        ApplicationReliabilityRuntime.ClearClient();
        _packetDispatcher.Reset();
        ContainerOpenResultTracker.Reset();
        FurnitureHammerResultTracker.Reset();
        CreativeFlyResultTracker.Reset();
        if (MySession != null) {
            ModNetworkManager.ClearSession(MySession);
            MySession.DisconnectedUtc = DateTime.UtcNow;
            MySession.DisconnectReason = disconnectInfo.Reason.ToString();
            NetworkSessionLifecycleTracker.RecordDisconnected(
                MySession,
                MySession.DisconnectReason);
            MySession.NetworkState = NetworkState.NotConnected;
        }

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
