using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using Engine;
using LiteNetLib;
using LiteNetLib.Utils;
namespace Game.Network;

public class NetworkServer : INetEventListener {
    private NetManager _netManager;
    private bool _disconnectByUser;
    public bool IsRunning { get; private set; } = false;
    public Dictionary<NetPeer, ClientSession> ClientSessions { get; set; } = [];

    public void Start() {
        _netManager = new NetManager(this) {
            MaxConnectAttempts = 6,
            DisconnectTimeout = 10000,
            ChannelsCount = NetworkChannels.Count,
            UnconnectedMessagesEnabled = true,
            ReuseAddress = true
        };

        if (!_netManager.Start(SettingsManager.ServerPort)) {
            throw new InvalidOperationException($"Failed to start server on port {SettingsManager.ServerPort}.");
        }

        IsRunning = true;
        Log.Information("Server started");
    }

    public void Stop() {
        _disconnectByUser = true;
        _netManager.DisconnectAll();
        _netManager.PollEvents();
        _netManager.Stop();

        IsRunning = false;
        Log.Information("Server stopped.");
    }

    public void Update() {
        _netManager.PollEvents();
        DateTime now = DateTime.UtcNow;
        foreach (KeyValuePair<NetPeer, ClientSession> item in ClientSessions.ToArray()) {
            ClientSession session = item.Value;
            if (session.CompatibilityState != NetworkCompatibilityState.Accepted
                && session.CompatibilityStartedUtc != default
                && now - session.CompatibilityStartedUtc >= NetworkCompatibilityRuntime.ServerHandshakeTimeout) {
                NetworkSessionLifecycleTracker.RecordCompatibilityRejected(
                    session,
                    NetworkCompatibilityIssueCode.HandshakeTimeout.ToString());
                Log.Warning($"Disconnecting {item.Key}: compatibility handshake timed out.");
                item.Key.Disconnect();
            }
            else if (NetworkSessionAdmissionRuntime.HasServerTimedOut(session, now)) {
                NetworkSessionAdmissionRuntime.RejectServerSession(
                    session,
                    NetworkSessionAdmission.CreateTimeoutResult(
                        (int)NetworkSessionAdmissionRuntime.ServerAdmissionTimeout.TotalSeconds));
            }
        }
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
        if (packet.MinNeedState <= session.NetworkState
            && CanSend(session, packet)) {
            PacketStreamWriter writer = new();
            writer.Write(PacketFragment.VerifyByte);
            writer.Write(packet.ID);
            packet.Serialize(new PacketSerializer(writer));

            NetPeer peer = ClientSessions.FirstOrDefault(kvp => kvp.Value == session).Key;
            if (peer == null) return;
            foreach (NetDataWriter chunk in PacketFragment.FragmentPayload(writer.Data())) {
                NetPeer targetPeer = peer;
                NetworkFaultInjector.Send(
                    chunk,
                    data => targetPeer.Send(data, packet.Channel, DeliveryMethod.ReliableOrdered),
                    packet.GetType().Name);
            }
        }
    }

    public void OnConnectionRequest(ConnectionRequest request) {
        Log.Information($"Connection request by: {request.RemoteEndPoint}");
        request.AcceptIfKey("x26.08.01");
    }

    public void OnNetworkError(IPEndPoint endPoint, SocketError socketError) {
        Log.Error($"Network error on {endPoint}: {socketError}");
    }

    public void OnNetworkLatencyUpdate(NetPeer peer, int latency) {
        // 可以用来计算延迟
    }

    public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod) {
        if (PacketManager.PackageFragment.TryProcessIncoming(reader, peer.ToString(), out byte[] payload)) {
            PacketManager.DecodePackets(payload).ForEach((packet) => {
                if (!ClientSessions.TryGetValue(peer, out ClientSession session)) return;
                if (session.CompatibilityState != NetworkCompatibilityState.Accepted) {
                    if (packet is not CompatibilityHelloPacket
                        && packet is not ModChannelCatalogPacket) {
                        NetworkCompatibilityRuntime.RejectPrematurePacket(session, packet);
                        return;
                    }
                }
                else if (session.AdmissionState != NetworkSessionAdmissionState.Accepted
                    && packet is not ConnectPacket) {
                    NetworkSessionAdmissionRuntime.RejectPrematurePacket(session, packet);
                    return;
                }
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
            TransportKind = "direct-server",
            PlayerIndex = -1,
            NetworkGuid = Guid.Empty,
            NetworkState = NetworkState.Connected,
            CompatibilityState = NetworkCompatibilityState.AwaitingHello,
            CompatibilityStartedUtc = DateTime.UtcNow
        };
        ClientSessions[peer] = session;
        NetworkSessionLifecycleTracker.RecordConnected(session);

        Log.Information($"Connected: {peer}");
    }

    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo) {
        if (ClientSessions.TryGetValue(peer, out ClientSession session)) {
            CleanupSession(session, disconnectInfo.Reason.ToString());
        }

        ClientSessions.Remove(peer);
        if (!_disconnectByUser) {
            Log.Information($"Disconnected: {peer}, {disconnectInfo}");
        }
    }

    public bool DisconnectSession(ClientSession session, string reason) {
        KeyValuePair<NetPeer, ClientSession> match = ClientSessions.FirstOrDefault(
            item => item.Value == session);
        if (match.Key == null) {
            return false;
        }
        CleanupSession(session, reason);
        ClientSessions.Remove(match.Key);
        match.Key.Disconnect();
        return true;
    }

    private static bool CanSend(ClientSession session, Packet packet) {
        if (packet is CompatibilityResultPacket or ModChannelCatalogResultPacket) {
            return true;
        }
        if (session.CompatibilityState != NetworkCompatibilityState.Accepted) {
            return false;
        }
        return packet is SessionAdmissionResultPacket
            || session.AdmissionState == NetworkSessionAdmissionState.Accepted;
    }

    private static void CleanupSession(ClientSession session, string reason) {
        if (session == null || session.NetworkState == NetworkState.NotConnected) {
            return;
        }
        int playerIndex = session.PlayerIndex;
        GameManager.Project?.FindSubsystem<SubsystemNetwork>().MakePlayerOffline(session);
        PlayerBodyUpdatePacket.RemoveSnapshotsForSession(session);
        NetworkDigConfirmationRuntime.ClearServerSession(playerIndex);
        FurnitureHammerPacket.ClearPlayerState(playerIndex);
        PlayerPacketBase.ClearHitThrottleState(playerIndex);
        ModNetworkManager.ClearSession(session);
        session.DisconnectedUtc = DateTime.UtcNow;
        session.DisconnectReason = reason ?? string.Empty;
        NetworkSessionLifecycleTracker.RecordDisconnected(session, session.DisconnectReason);
        session.PlayerIndex = -1;
        session.NetworkState = NetworkState.NotConnected;
    }
}
