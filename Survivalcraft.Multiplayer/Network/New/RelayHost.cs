using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Engine;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Game.Network;

public sealed class RelayHost : INetEventListener {
    private readonly Dictionary<int, ClientSession> _sessions = [];
    private bool _disconnectByUser;
    private NetManager _netManager;
    private NetPeer _relayPeer;
    private bool _isRetryed;
    public bool IsConnected { get; private set; }
    public bool IsDisconnected { get; private set; }
    public string RoomCode { get; set; }
    public string GlobalCode { get; set; }
    public IEnumerable<ClientSession> Sessions => _sessions.Values;

    public void Start(IPEndPoint relayAddress) {
        _netManager = new NetManager(this) {
            MaxConnectAttempts = 6,
            DisconnectTimeout = 10000,
            ChannelsCount = NetworkChannels.Count,
        };

        if (!_netManager.Start()) {
            throw new InvalidOperationException("Failed to start relay host transport.");
        }
        _netManager.Connect(relayAddress, RelayProtocol.ConnectionKey);
        Log.Information($"Connecting relay {relayAddress}");
    }

    public void Update() {
        _netManager.PollEvents();
        DateTime now = DateTime.UtcNow;
        foreach (KeyValuePair<int, ClientSession> item in _sessions.ToArray()) {
            ClientSession session = item.Value;
            if (session.CompatibilityState != NetworkCompatibilityState.Accepted
                && session.CompatibilityStartedUtc != default
                && now - session.CompatibilityStartedUtc >= NetworkCompatibilityRuntime.ServerHandshakeTimeout) {
                session.CompatibilityResult = NetworkCompatibility.CreateHandshakeTimeoutResult(
                    (int)NetworkCompatibilityRuntime.ServerHandshakeTimeout.TotalSeconds,
                    session.CompatibilityState);
                session.CompatibilityState = NetworkCompatibilityState.Rejected;
                session.CompatibilityStartedUtc = default;
                Log.Warning(
                    $"Disconnecting relay peer {item.Key}: compatibility handshake timed out.");
                NetworkSessionLifecycleTracker.RecordCompatibilityRejected(
                    session,
                    NetworkCompatibilityIssueCode.HandshakeTimeout.ToString());
                DisconnectSession(item.Key, session, "Compatibility handshake timed out.");
            }
            else if (NetworkSessionAdmissionRuntime.HasServerTimedOut(session, now)) {
                NetworkSessionAdmissionRuntime.RejectServerSession(
                    session,
                    NetworkSessionAdmission.CreateTimeoutResult(
                        (int)NetworkSessionAdmissionRuntime.ServerAdmissionTimeout.TotalSeconds));
            }
        }
    }

    public void Disconnect() {
        _disconnectByUser = true;
        _netManager.DisconnectAll();
        _netManager.PollEvents();
        _netManager.Stop();
    }

    public void Board(Packet packet) {
        if (packet.To != null) Send(packet, packet.To);
        else foreach (ClientSession session in _sessions.Values.ToArray()) if (session != packet.Except) Send(packet, session);
    }

    public void Send(Packet packet, ClientSession session) {
        if (!_sessions.Values.Contains(session)
            || packet.MinNeedState > session.NetworkState
            || !CanSend(session, packet)) {
            return;
        }
        string logicalPacketType = packet.GetType().Name;
        Packet outbound = ApplicationReliabilityRuntime.ProtectServer(packet, session);
        if (outbound != null) {
            SendPrepared(outbound, session, logicalPacketType);
        }
    }

    internal bool SendPrepared(
        Packet packet,
        ClientSession session,
        string logicalPacketType) {
        if (packet.MinNeedState > session.NetworkState || !CanSend(session, packet)) {
            return false;
        }
        int connectionId = _sessions.FirstOrDefault(p => p.Value == session).Key;
        if (connectionId == 0) return false;
        PacketStreamWriter payload = new();
        payload.Write(PacketFragment.VerifyByte);
        payload.Write(packet.ID);
        packet.Serialize(new PacketSerializer(payload));
        foreach (NetDataWriter chunk in PacketFragment.FragmentPayload(payload.Data())) {
            NetDataWriter frame = RelayProtocol.Begin(RelayMessageType.DataToPeer);
            frame.Put(connectionId);
            frame.PutBytesWithLength(chunk.CopyData());
            if (_relayPeer != null) {
                NetPeer relayPeer = _relayPeer;
                NetworkFaultInjector.Send(
                    frame,
                    data => relayPeer.Send(data, packet.Channel, DeliveryMethod.ReliableOrdered),
                    logicalPacketType,
                    packet is ApplicationReliablePacket { Kind: ApplicationReliableFrameKind.Data });
            }
        }
        return _relayPeer != null;
    }

    public void OnPeerConnected(NetPeer peer) {
        _relayPeer = peer;
        peer.Send(RelayProtocol.Begin(RelayMessageType.CreateRoom), DeliveryMethod.ReliableOrdered);
    }

    public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod) {
        if (!RelayProtocol.TryReadHeader(reader, out RelayMessageType type)) return;
        if (type == RelayMessageType.RoomCreated) {
            RoomCode = reader.GetString();
            GlobalCode = reader.AvailableBytes > 0 ? reader.GetString() : null;
            IsConnected = true;
            Log.Information(string.IsNullOrWhiteSpace(GlobalCode)
                ? $"Relay room created: {RoomCode}"
                : $"Relay room created: {RoomCode}, global={GlobalCode}");
            NetworkManager.RelayHost = this;
            NetworkManager.RelayHostCodeStatus = RelayHostCodeStatus.Ready;
        }
        else if (type == RelayMessageType.PeerJoined) {
            int id = reader.GetInt();
            Log.Information($"Relay peer joined: connection={id}");
            _sessions[id] = new ClientSession {
                TransportKind = "relay-host",
                PlayerIndex = -1,
                NetworkGuid = Guid.Empty,
                NetworkState = NetworkState.Connected,
                CompatibilityState = NetworkCompatibilityState.AwaitingHello,
                CompatibilityStartedUtc = DateTime.UtcNow
            };
            NetworkSessionLifecycleTracker.RecordConnected(_sessions[id]);
        }
        else if (type == RelayMessageType.PeerLeft) {
            int id = reader.GetInt();
            if (_sessions.Remove(id, out ClientSession session)) {
                CleanupSession(session, "Relay peer left.");
            }
        }
        else if (type == RelayMessageType.DataToHost) {
            int id = reader.GetInt();
            // PeerJoined and the peer's first payload travel over different UDP
            // connections and can therefore be observed in either order.
            if (!_sessions.TryGetValue(id, out ClientSession session)) {
                session = new ClientSession {
                    TransportKind = "relay-host",
                    PlayerIndex = -1,
                    NetworkGuid = Guid.Empty,
                    NetworkState = NetworkState.Connected,
                    CompatibilityState = NetworkCompatibilityState.AwaitingHello,
                    CompatibilityStartedUtc = DateTime.UtcNow
                };
                _sessions[id] = session;
                NetworkSessionLifecycleTracker.RecordConnected(session);
            }
            byte[] data = reader.GetBytesWithLength();
            NetDataReader inner = new(data);
            if (PacketManager.PackageFragment.TryProcessIncoming(inner, $"relay-{id}", out byte[] payload)) {
                foreach (Packet packet in PacketManager.DecodePackets(payload)) {
                    if (session.CompatibilityState != NetworkCompatibilityState.Accepted) {
                        if (packet is not CompatibilityHelloPacket
                            && packet is not ModChannelCatalogPacket) {
                            NetworkCompatibilityRuntime.RejectPrematurePacket(session, packet);
                            continue;
                        }
                    }
                    else if (session.AdmissionState != NetworkSessionAdmissionState.Accepted
                        && packet is not ConnectPacket) {
                        NetworkSessionAdmissionRuntime.RejectPrematurePacket(session, packet);
                        continue;
                    }
                    if (packet.MinNeedState <= session.NetworkState) {
                        packet.From = session;
                        packet.Handle(true);
                    }
                }
            }
        }
    }

    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo) {
        IsConnected = false;
        IsDisconnected = true;

        PlayerBodyUpdatePacket.ClearSnapshots();
        foreach (ClientSession session in _sessions.Values.ToArray()) {
            CleanupSession(session, disconnectInfo.Reason.ToString());
        }
        _sessions.Clear();

        NetworkManager.RelayHostCodeStatus = RelayHostCodeStatus.Failed;
        Log.Information($"Relay disconnected: {peer.Address}, Reason: {disconnectInfo.Reason}");

        if (!_isRetryed && !_disconnectByUser) {
            _isRetryed = true;
            Log.Information("Attempting to retry to create relay host.");
            NetworkManager.RelayHostCodeStatus = RelayHostCodeStatus.Creating;

            Task.Run(async () => {
                IPEndPoint endPoint = await CenterServerClient.GetBestRelayEndPointAsync(2);
                Dispatcher.Dispatch(() => Start(endPoint));
            });
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

    public bool DisconnectSession(ClientSession session, string reason) {
        KeyValuePair<int, ClientSession> match = _sessions.FirstOrDefault(
            item => item.Value == session);
        if (match.Key == 0) {
            return false;
        }
        DisconnectSession(match.Key, session, reason);
        return true;
    }

    private void DisconnectSession(int connectionId, ClientSession session, string reason) {
        CleanupSession(session, reason);
        _sessions.Remove(connectionId);
        if (_relayPeer != null) {
            NetDataWriter writer = RelayProtocol.Begin(RelayMessageType.DisconnectPeer);
            writer.Put(connectionId);
            _relayPeer.Send(writer, DeliveryMethod.ReliableOrdered);
        }
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
        ApplicationReliabilityRuntime.ClearServerSession(session);
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
