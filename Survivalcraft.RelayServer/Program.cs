using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using LiteNetLib;
using LiteNetLib.Utils;

const int defaultPort = 28888;
int port = args.Length > 0 && int.TryParse(args[0], out int parsed) ? parsed : defaultPort;
RelayCenterOptions centerOptions = RelayCenterOptions.From(args, port);
using CancellationTokenSource cancellation = new();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };

RelayServer server = new(centerOptions);
if (!server.Start(port)) return 1;
Console.WriteLine($"Survivalcraft relay listening on UDP {port}. Press Ctrl+C to stop.");
if (centerOptions.Enabled) Console.WriteLine($"Center web server enabled: {centerOptions.CenterUrl}, relayId={centerOptions.RelayId}");
while (!cancellation.IsCancellationRequested) {
    server.PollEvents();
    Thread.Sleep(10);
}
server.Stop();
return 0;

internal enum RelayMessageType : byte { CreateRoom = 1, RoomCreated, JoinRoom, JoinAccepted, JoinRejected, PeerJoined, PeerLeft, DataToHost, DataToPeer, CloseRoom }

internal static class RelayProtocol {
    public const uint Magic = 0x5343524C;
    public const byte Version = 1;
    public const string ConnectionKey = "sc-relay-1";
    public const int MaxPayloadLength = 64 * 1024;
    public static NetDataWriter Begin(RelayMessageType type) { NetDataWriter w = new(); w.Put(Magic); w.Put(Version); w.Put((byte)type); return w; }
    public static bool Read(NetDataReader r, out RelayMessageType type) { type = default; if (r.AvailableBytes < 6 || r.GetUInt() != Magic || r.GetByte() != Version) return false; type = (RelayMessageType)r.GetByte(); return true; }
}

internal sealed class Room {
    public required string Code { get; init; }
    public string? GlobalCode { get; set; }
    public required NetPeer Host { get; init; }
    public Dictionary<int, NetPeer> Peers { get; } = [];
    public int NextConnectionId { get; set; } = 1;
    public DateTime LastHeartbeatUtc { get; set; } = DateTime.MinValue;
}

internal sealed class RelayServer(RelayCenterOptions centerOptions) : INetEventListener {
    private readonly Dictionary<string, Room> rooms = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<NetPeer, Room> memberships = [];
    private readonly RelayCenterClient centerClient = new(centerOptions);
    private readonly Random random = new();
    private readonly Queue<Action> mainThreadActions = [];
    private readonly object mainThreadActionsLock = new();
    private DateTime lastRelayHeartbeatUtc = DateTime.MinValue;
    private NetManager? manager;

    public bool Start(int port) { manager = new NetManager(this) { DisconnectTimeout = 15000, UpdateTime = 15, ReuseAddress = true }; return manager.Start(port); }
    public void PollEvents() {
        DrainMainThreadActions();
        manager?.PollEvents();
        DrainMainThreadActions();
        UpdateCenter();
    }
    public void Stop() => manager?.Stop(true);
    public void OnConnectionRequest(ConnectionRequest request) => request.AcceptIfKey(RelayProtocol.ConnectionKey);

    public void OnNetworkReceive(NetPeer sender, NetPacketReader reader, byte channel, DeliveryMethod method) {
        if (!RelayProtocol.Read(reader, out RelayMessageType type)) return;
        try {
            switch (type) {
                case RelayMessageType.CreateRoom: CreateRoom(sender); break;
                case RelayMessageType.JoinRoom: JoinRoom(sender, reader.GetString(16)); break;
                case RelayMessageType.DataToHost: ForwardToHost(sender, reader); break;
                case RelayMessageType.DataToPeer: ForwardToPeer(sender, reader); break;
                case RelayMessageType.CloseRoom: Remove(sender); break;
            }
        } catch (Exception ex) { Console.Error.WriteLine($"Rejected frame from {sender.Address}: {ex.Message}"); }
    }

    private void CreateRoom(NetPeer host) {
        Remove(host);
        string code;
        do code = random.Next(0, 36 * 36 * 36 * 36).ToString("X4"); while (rooms.ContainsKey(code));
        Room room = new() { Code = code, Host = host };
        rooms[code] = room; memberships[host] = room;
        _ = RegisterRoomAndReplyAsync(room);
    }

    private async Task RegisterRoomAndReplyAsync(Room room) {
        string? globalCode = null;
        try {
            globalCode = await centerClient.RegisterRoomAsync(room.Code, 32, CancellationToken.None);
        }
        catch (Exception ex) {
            Console.Error.WriteLine($"Center room registration failed for {room.Code}: {ex.Message}");
        }

        EnqueueMainThreadAction(() => CompleteRoomCreation(room, globalCode));
    }

    private void CompleteRoomCreation(Room room, string? globalCode) {
        if (!memberships.TryGetValue(room.Host, out Room? currentRoom)
            || !ReferenceEquals(currentRoom, room)
            || !rooms.TryGetValue(room.Code, out Room? registeredRoom)
            || !ReferenceEquals(registeredRoom, room)) {
            _ = centerClient.DeleteRoomAsync(globalCode, CancellationToken.None);
            return;
        }

        room.GlobalCode = globalCode;
        NetDataWriter reply = RelayProtocol.Begin(RelayMessageType.RoomCreated);
        reply.Put(room.Code);
        if (!string.IsNullOrWhiteSpace(room.GlobalCode)) reply.Put(room.GlobalCode);
        room.Host.Send(reply, DeliveryMethod.ReliableOrdered);
        Console.WriteLine(string.IsNullOrWhiteSpace(room.GlobalCode)
            ? $"Room {room.Code} created by {room.Host.Address}"
            : $"Room {room.Code} created by {room.Host.Address}, global={room.GlobalCode}");
    }

    private void JoinRoom(NetPeer peer, string code) {
        Remove(peer);
        if (!rooms.TryGetValue(code, out Room? room) || room.Peers.Count >= 32) { Reject(peer, "房间不存在或已满"); return; }
        int id = room.NextConnectionId++; room.Peers[id] = peer; memberships[peer] = room;
        NetDataWriter accepted = RelayProtocol.Begin(RelayMessageType.JoinAccepted); accepted.Put(id); peer.Send(accepted, DeliveryMethod.ReliableOrdered);
        NetDataWriter joined = RelayProtocol.Begin(RelayMessageType.PeerJoined); joined.Put(id); room.Host.Send(joined, DeliveryMethod.ReliableOrdered);
    }

    private void ForwardToHost(NetPeer peer, NetPacketReader reader) {
        if (!memberships.TryGetValue(peer, out Room? room) || peer == room.Host) return;
        int id = room.Peers.FirstOrDefault(p => p.Value == peer).Key; if (id == 0) return;
        byte[] payload = ReadPayload(reader);
        NetDataWriter frame = RelayProtocol.Begin(RelayMessageType.DataToHost); frame.Put(id); frame.PutBytesWithLength(payload); room.Host.Send(frame, DeliveryMethod.ReliableOrdered);
    }

    private void ForwardToPeer(NetPeer host, NetPacketReader reader) {
        if (!memberships.TryGetValue(host, out Room? room) || host != room.Host) return;
        int id = reader.GetInt(); byte[] payload = ReadPayload(reader);
        if (room.Peers.TryGetValue(id, out NetPeer? peer)) { NetDataWriter frame = RelayProtocol.Begin(RelayMessageType.DataToPeer); frame.PutBytesWithLength(payload); peer.Send(frame, DeliveryMethod.ReliableOrdered); }
    }

    private static byte[] ReadPayload(NetDataReader reader) { byte[] data = reader.GetBytesWithLength(); if (data.Length > RelayProtocol.MaxPayloadLength) throw new InvalidDataException("payload too large"); return data; }
    private static void Reject(NetPeer peer, string reason) { NetDataWriter w = RelayProtocol.Begin(RelayMessageType.JoinRejected); w.Put(reason); peer.Send(w, DeliveryMethod.ReliableOrdered); }

    private void Remove(NetPeer peer) {
        if (!memberships.Remove(peer, out Room? room)) return;
        if (peer == room.Host) {
            foreach (NetPeer p in room.Peers.Values) p.Disconnect();
            rooms.Remove(room.Code);
            _ = centerClient.DeleteRoomAsync(room.GlobalCode, CancellationToken.None);
            return;
        }
        int id = room.Peers.FirstOrDefault(p => p.Value == peer).Key;
        if (id != 0) { room.Peers.Remove(id); NetDataWriter left = RelayProtocol.Begin(RelayMessageType.PeerLeft); left.Put(id); room.Host.Send(left, DeliveryMethod.ReliableOrdered); }
    }

    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo info) => Remove(peer);
    public void OnPeerConnected(NetPeer peer) { }
    public void OnNetworkError(IPEndPoint endPoint, SocketError error) => Console.Error.WriteLine($"{endPoint}: {error}");
    public void OnNetworkLatencyUpdate(NetPeer peer, int latency) { }
    public void OnNetworkReceiveUnconnected(IPEndPoint remote, NetPacketReader reader, UnconnectedMessageType type) { }

    private void UpdateCenter() {
        if (!centerClient.Enabled) return;

        DateTime now = DateTime.UtcNow;
        if (now - lastRelayHeartbeatUtc >= centerClient.RelayHeartbeatInterval) {
            lastRelayHeartbeatUtc = now;
            _ = centerClient.SendRelayHeartbeatAsync(rooms.Count, rooms.Sum(r => r.Value.Peers.Count), CancellationToken.None);
        }

        foreach (Room room in rooms.Values) {
            if (string.IsNullOrWhiteSpace(room.GlobalCode)) continue;
            if (now - room.LastHeartbeatUtc < centerClient.RoomHeartbeatInterval) continue;
            room.LastHeartbeatUtc = now;
            _ = centerClient.SendRoomHeartbeatAsync(room.GlobalCode, CancellationToken.None);
        }
    }

    private void EnqueueMainThreadAction(Action action) {
        lock (mainThreadActionsLock) {
            mainThreadActions.Enqueue(action);
        }
    }

    private void DrainMainThreadActions() {
        while (true) {
            Action? action;
            lock (mainThreadActionsLock) {
                if (mainThreadActions.Count == 0) return;
                action = mainThreadActions.Dequeue();
            }
            try {
                action();
            }
            catch (Exception ex) {
                Console.Error.WriteLine($"Relay action failed: {ex.Message}");
            }
        }
    }
}

internal sealed record RelayCenterOptions(
    string? CenterUrl,
    string? RelayToken,
    string RelayId,
    string PublicHost,
    int PublicUdpPort,
    string? Region,
    string? Version,
    int Priority) {
    public bool Enabled => !string.IsNullOrWhiteSpace(CenterUrl);

    public static RelayCenterOptions From(string[] args, int listenPort) {
        string? centerUrl = GetArg(args, 1) ?? Environment.GetEnvironmentVariable("SC_CENTER_URL");
        string? relayToken = GetArg(args, 2) ?? Environment.GetEnvironmentVariable("SC_CENTER_RELAY_TOKEN");
        string publicHost = GetArg(args, 3)
            ?? Environment.GetEnvironmentVariable("SC_RELAY_PUBLIC_HOST")
            ?? Environment.MachineName;
        int publicUdpPort = TryReadInt(GetArg(args, 4) ?? Environment.GetEnvironmentVariable("SC_RELAY_PUBLIC_UDP_PORT"), listenPort);
        string relayId = GetArg(args, 5)
            ?? Environment.GetEnvironmentVariable("SC_RELAY_ID")
            ?? $"{publicHost}:{publicUdpPort}";
        string? region = Environment.GetEnvironmentVariable("SC_RELAY_REGION");
        string? version = Environment.GetEnvironmentVariable("SC_RELAY_VERSION") ?? RelayProtocol.Version.ToString();
        int priority = TryReadInt(Environment.GetEnvironmentVariable("SC_RELAY_PRIORITY"), 0);
        return new RelayCenterOptions(NormalizeUrl(centerUrl), relayToken, relayId, publicHost, publicUdpPort, region, version, priority);
    }

    private static string? GetArg(string[] args, int index) => args.Length > index && !string.IsNullOrWhiteSpace(args[index]) ? args[index] : null;
    private static int TryReadInt(string? value, int fallback) => int.TryParse(value, out int result) ? result : fallback;
    private static string? NormalizeUrl(string? url) => string.IsNullOrWhiteSpace(url) ? null : url.Trim().TrimEnd('/');
}

internal sealed class RelayCenterClient(RelayCenterOptions options) {
    private readonly HttpClient client = new() { Timeout = TimeSpan.FromSeconds(2) };
    public bool Enabled => options.Enabled;
    public TimeSpan RelayHeartbeatInterval { get; } = TimeSpan.FromSeconds(15);
    public TimeSpan RoomHeartbeatInterval { get; } = TimeSpan.FromSeconds(15);

    public async Task SendRelayHeartbeatAsync(int roomCount, int peerCount, CancellationToken cancellationToken) {
        if (!Enabled) return;
        try {
            using HttpRequestMessage request = CreateRequest(HttpMethod.Post, "/api/v1/relays/heartbeat");
            request.Content = JsonContent.Create(new RelayHeartbeatRequest(
                options.RelayId,
                options.PublicHost,
                options.PublicUdpPort,
                options.Region,
                options.Version,
                roomCount,
                peerCount,
                options.Priority,
                true));
            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex) {
            Console.Error.WriteLine($"Center relay heartbeat failed: {ex.Message}");
        }
    }

    public async Task<string?> RegisterRoomAsync(string relayRoomCode, int maxPlayers, CancellationToken cancellationToken) {
        if (!Enabled) return null;
        await SendRelayHeartbeatAsync(0, 0, cancellationToken);

        using HttpRequestMessage request = CreateRequest(HttpMethod.Post, "/api/v1/rooms");
        request.Content = JsonContent.Create(new RoomRegistrationRequest(options.RelayId, relayRoomCode, maxPlayers, 120));
        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        RoomRegistrationResponse? result = await response.Content.ReadFromJsonAsync<RoomRegistrationResponse>(cancellationToken);
        return result?.GlobalCode;
    }

    public async Task SendRoomHeartbeatAsync(string? globalCode, CancellationToken cancellationToken) {
        if (!Enabled || string.IsNullOrWhiteSpace(globalCode)) return;
        try {
            using HttpRequestMessage request = CreateRequest(HttpMethod.Post, $"/api/v1/rooms/{Uri.EscapeDataString(globalCode)}/heartbeat");
            request.Content = JsonContent.Create(new RoomHeartbeatRequest(options.RelayId, 120));
            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex) {
            Console.Error.WriteLine($"Center room heartbeat failed for {globalCode}: {ex.Message}");
        }
    }

    public async Task DeleteRoomAsync(string? globalCode, CancellationToken cancellationToken) {
        if (!Enabled || string.IsNullOrWhiteSpace(globalCode)) return;
        try {
            using HttpRequestMessage request = CreateRequest(HttpMethod.Delete, $"/api/v1/rooms/{Uri.EscapeDataString(globalCode)}");
            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex) {
            Console.Error.WriteLine($"Center room delete failed for {globalCode}: {ex.Message}");
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path) {
        HttpRequestMessage request = new(method, options.CenterUrl + path);
        if (!string.IsNullOrWhiteSpace(options.RelayToken)) request.Headers.TryAddWithoutValidation("X-Relay-Token", options.RelayToken);
        return request;
    }

    private sealed record RelayHeartbeatRequest(
        string RelayId,
        string PublicHost,
        int UdpPort,
        string? Region,
        string? Version,
        int RoomCount,
        int PeerCount,
        int Priority,
        bool Enabled);

    private sealed record RoomRegistrationRequest(string RelayId, string RelayRoomCode, int MaxPlayers, int TtlSeconds);
    private sealed record RoomRegistrationResponse(string GlobalCode, DateTimeOffset ExpiresAtUtc, int HeartbeatIntervalSeconds);
    private sealed record RoomHeartbeatRequest(string RelayId, int TtlSeconds);
}
