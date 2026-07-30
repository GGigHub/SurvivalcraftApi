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
                _sessions.Remove(item.Key);
                ModNetworkManager.ClearSession(session);
                PlayerBodyUpdatePacket.RemoveSnapshotsForSession(session);
                GameManager.Project?.FindSubsystem<SubsystemNetwork>().MakePlayerOffline(session);
                if (_relayPeer != null) {
                    NetDataWriter writer = RelayProtocol.Begin(RelayMessageType.DisconnectPeer);
                    writer.Put(item.Key);
                    _relayPeer.Send(writer, DeliveryMethod.ReliableOrdered);
                }
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
        if (packet.MinNeedState > session.NetworkState
            || (session.CompatibilityState != NetworkCompatibilityState.Accepted
                && packet is not CompatibilityResultPacket
                && packet is not ModChannelCatalogResultPacket)) return;
        int connectionId = _sessions.FirstOrDefault(p => p.Value == session).Key;
        if (connectionId == 0) return;
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
                    data => relayPeer.Send(data, DeliveryMethod.ReliableOrdered));
            }
        }
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
                PlayerIndex = -1,
                NetworkGuid = Guid.Empty,
                NetworkState = NetworkState.Connected,
                CompatibilityState = NetworkCompatibilityState.AwaitingHello,
                CompatibilityStartedUtc = DateTime.UtcNow
            };
        }
        else if (type == RelayMessageType.PeerLeft) {
            int id = reader.GetInt();
            if (_sessions.Remove(id, out ClientSession session)) {
                ModNetworkManager.ClearSession(session);
                PlayerBodyUpdatePacket.RemoveSnapshotsForSession(session);
                GameManager.Project?.FindSubsystem<SubsystemNetwork>().MakePlayerOffline(session);
            }
        }
        else if (type == RelayMessageType.DataToHost) {
            int id = reader.GetInt();
            // PeerJoined and the peer's first payload travel over different UDP
            // connections and can therefore be observed in either order.
            if (!_sessions.TryGetValue(id, out ClientSession session)) {
                session = new ClientSession {
                    PlayerIndex = -1,
                    NetworkGuid = Guid.Empty,
                    NetworkState = NetworkState.Connected,
                    CompatibilityState = NetworkCompatibilityState.AwaitingHello,
                    CompatibilityStartedUtc = DateTime.UtcNow
                };
                _sessions[id] = session;
            }
            byte[] data = reader.GetBytesWithLength();
            NetDataReader inner = new(data);
            if (PacketManager.PackageFragment.TryProcessIncoming(inner, $"relay-{id}", out byte[] payload)) {
                foreach (Packet packet in PacketManager.DecodePackets(payload)) {
                    if (session.CompatibilityState != NetworkCompatibilityState.Accepted
                        && packet is not CompatibilityHelloPacket
                        && packet is not ModChannelCatalogPacket
                        && packet is not ConnectPacket) {
                        NetworkCompatibilityRuntime.RejectPrematurePacket(session, packet);
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
        foreach (ClientSession session in _sessions.Values) {
            ModNetworkManager.ClearSession(session);
        }
        _sessions.Clear();
        GameManager.Project?.FindSubsystem<SubsystemNetwork>().MakeAllPlayersOffline();

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
}
