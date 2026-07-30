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

    public ClientSession MySession { get; set; }
    public bool IsConnected { get; set; }
    public bool IsRunning => _netManager?.IsRunning == true;

    public void Connect(IPEndPoint serverAddress) {
        _netManager = new NetManager(this) {
            MaxConnectAttempts = 6,
            DisconnectTimeout = 10000,
            UnconnectedMessagesEnabled = true,
            ReuseAddress = true
        };
        if (!_netManager.Start()) {
            throw new InvalidOperationException("Failed to start client transport.");
        }

        _serverAddress = serverAddress;
        _netManager.Connect(_serverAddress, "x26.06.01");

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
    }

    public void Send(Packet packet) {
        if (MySession != null
            && MySession.CompatibilityState != NetworkCompatibilityState.Accepted
            && packet is not CompatibilityHelloPacket
            && packet is not ModChannelCatalogPacket) {
            return;
        }
        PacketStreamWriter writer = new();
        writer.Write(PacketFragment.VerifyByte);
        writer.Write(packet.ID);
        packet.Serialize(new PacketSerializer(writer));

        foreach (NetDataWriter chunk in PacketFragment.FragmentPayload(writer.Data())) {
            if (_serverPeer != null) {
                NetPeer peer = _serverPeer;
                NetworkFaultInjector.Send(
                    chunk,
                    data => peer.Send(data, DeliveryMethod.ReliableOrdered),
                    packet.GetType().Name);
            }
        }
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
                if (MySession?.CompatibilityState != NetworkCompatibilityState.Accepted
                    && packet is not CompatibilityResultPacket
                    && packet is not ModChannelCatalogResultPacket) {
                    return;
                }
                if (packet.MinNeedState >= NetworkState.ProjectLoaded && GameManager.Project == null) {
                    return;
                }
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
            NetworkGuid = Guid.Parse(SettingsManager.OnlineAccessToken),
            NetworkState = NetworkState.NotConnected,
            CompatibilityState = NetworkCompatibilityState.AwaitingHello
        };
        IsConnected = true;
        NetworkCompatibilityManifest manifest = NetworkCompatibilityRuntime.BeginClientHandshake(MySession);
        Send(new CompatibilityHelloPacket(manifest));
    }

    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo) {
        IsConnected = false;
        if (MySession != null) {
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
