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
    private NetManager _netManager;
    private NetPeer _relayPeer;
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
    }

    public void Disconnect() {
        _sessions.Clear();
        _relayPeer = null;
        _netManager.Stop();
        IsConnected = false;
        IsDisconnected = true;
        GameManager.Project?.FindSubsystem<SubsystemNetwork>().MakeAllPlayersOffline();
    }

    public void Board(Packet packet) {
        if (packet.To != null) Send(packet, packet.To);
        else foreach (ClientSession session in _sessions.Values.ToArray()) if (session != packet.Except) Send(packet, session);
    }

    public void Send(Packet packet, ClientSession session) {
        if (packet.MinNeedState > session.NetworkState) return;
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
            _relayPeer?.Send(frame, DeliveryMethod.ReliableOrdered);
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
                NetworkState = NetworkState.Connected
            };
        }
        else if (type == RelayMessageType.PeerLeft) {
            int id = reader.GetInt();
            if (_sessions.Remove(id, out ClientSession session) && GameManager.Project != null)
                GameManager.Project.FindSubsystem<SubsystemNetwork>()?.MakePlayerOffline(session);
        }
        else if (type == RelayMessageType.DataToHost) {
            int id = reader.GetInt();
            // PeerJoined and the peer's first payload travel over different UDP
            // connections and can therefore be observed in either order.
            if (!_sessions.TryGetValue(id, out ClientSession session)) {
                session = new ClientSession {
                    PlayerIndex = -1,
                    NetworkGuid = Guid.Empty,
                    NetworkState = NetworkState.Connected
                };
                _sessions[id] = session;
            }
            byte[] data = reader.GetBytesWithLength();
            NetDataReader inner = new(data);
            if (NetworkManager.PackageFragment.TryProcessIncoming(inner, $"relay-{id}", out byte[] payload)) {
                foreach (Packet packet in PacketManager.DecodePackets(payload)) {
                    Log.Information($"Relay decoded packet {packet.ID} from connection={id}");
                    if (packet.MinNeedState <= session.NetworkState) {
                        packet.From = session;
                        packet.Handle(true);
                    }
                }
            }
        }
    }

    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo) {
        _relayPeer = null;
        _sessions.Clear();
        IsConnected = false;
        IsDisconnected = true;
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
