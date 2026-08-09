using System;
using System.Net;
using System.Net.Sockets;
using Engine;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Game.Network;

public class NetworkClient : INetEventListener {
    private NetPeer _serverPeer;
    private NetManager _netManager;
    private IPEndPoint _serverAddress;
    private bool _disconnectByUser;
    private readonly ClientPacketDispatcher _packetDispatcher = new();

    public ClientSession MySession { get; set; }
    public bool IsConnected { get; set; }
    public bool IsRunning => _netManager?.IsRunning == true;

    public void Connect(IPEndPoint serverAddress) {
        ApplicationReliabilityRuntime.ClearClient();
        _packetDispatcher.Reset();
        ContainerOpenResultTracker.Reset();
        FurnitureHammerResultTracker.Reset();
        CreativeFlyResultTracker.Reset();
        _netManager = new NetManager(this) {
            MaxConnectAttempts = 6,
            DisconnectTimeout = 10000,
            ChannelsCount = NetworkChannels.Count,
            UnconnectedMessagesEnabled = true,
            ReuseAddress = true
        };
        if (!_netManager.Start()) {
            throw new InvalidOperationException("Failed to start client transport.");
        }

        _serverAddress = serverAddress;
        _netManager.Connect(_serverAddress, "x26.08.01");

        Log.Information($"Connecting {_serverAddress}");
    }

    public void Disconnect() {
        _disconnectByUser = true;
        _netManager.DisconnectAll();
        _netManager.PollEvents();
        _netManager.Stop();
    }

    public void Update() {
        LatencyProbe.Update();
        _netManager.PollEvents();
        _packetDispatcher.Flush(MySession);
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
        PacketStreamWriter writer = new();
        writer.Write(PacketFragment.VerifyByte);
        writer.Write(packet.ID);
        packet.Serialize(new PacketSerializer(writer));

        bool sent = false;
        foreach (NetDataWriter chunk in PacketFragment.FragmentPayload(writer.Data())) {
            if (_serverPeer != null) {
                NetPeer peer = _serverPeer;
                NetworkFaultInjector.Send(
                    chunk,
                    data => peer.Send(data, packet.Channel, DeliveryMethod.ReliableOrdered),
                    logicalPacketType,
                    packet is ApplicationReliablePacket { Kind: ApplicationReliableFrameKind.Data });
                sent = true;
            }
        }
        return sent;
    }

    public void OnConnectionRequest(ConnectionRequest request) {
        Log.Information($"Connection request by: {request.RemoteEndPoint}");
    }

    public void OnNetworkError(IPEndPoint endPoint, SocketError socketError) {
        Log.Error($"Network error on: {socketError}");
    }

    public void OnNetworkLatencyUpdate(NetPeer peer, int latency) {
        // 可以用来计算网络延迟
    }

    public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod) {
        if (PacketManager.PackageFragment.TryProcessIncoming(reader, peer.ToString(), out byte[] payload)) {
            PacketManager.DecodePackets(payload).ForEach((packet) => {
                if (MySession?.CompatibilityState != NetworkCompatibilityState.Accepted) {
                    if (packet is not CompatibilityResultPacket
                        && packet is not ModChannelCatalogResultPacket) {
                        return;
                    }
                }
                else if (MySession.AdmissionState != NetworkSessionAdmissionState.Accepted
                    && packet is not SessionAdmissionResultPacket) {
                    return;
                }
                _packetDispatcher.Dispatch(MySession, packet);
            });
        }
    }

    public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) {
        Log.Information($"Unconnected receive by: {remoteEndPoint}");
    }

    public void OnPeerConnected(NetPeer peer) {
        ApplicationReliabilityRuntime.ClearClient();
        _packetDispatcher.Reset();
        ContainerOpenResultTracker.Reset();
        FurnitureHammerResultTracker.Reset();
        CreativeFlyResultTracker.Reset();
        _serverPeer = peer;
        Log.Information($"Connected: {_serverAddress}");

        MySession = new ClientSession {
            TransportKind = "direct-client",
            PlayerIndex = -1,
            NetworkGuid = Guid.Parse(SettingsManager.OnlineAccessToken),
            NetworkState = NetworkState.NotConnected,
            CompatibilityState = NetworkCompatibilityState.AwaitingHello
        };
        IsConnected = true;
        NetworkSessionLifecycleTracker.RecordConnected(MySession);
        NetworkCompatibilityManifest manifest = NetworkCompatibilityRuntime.BeginClientHandshake(MySession);
        Send(new CompatibilityHelloPacket(manifest));
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
        Log.Information($"Disconnected: {_serverAddress}, Reason: {disconnectInfo.Reason}");

        if (!_disconnectByUser) {
            GameManager.DisposeProject();
            ScreensManager.SwitchScreen("MainMenu");
            DialogsManager.Alert("Disconnected", disconnectInfo.Reason.ToString());
        }
    }
}
