using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCors(options => {
    options.AddPolicy("public-api", policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});
builder.Services.ConfigureHttpJsonOptions(options => {
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});
builder.Services.AddSingleton<CenterState>();

var app = builder.Build();
var options = CenterServerOptions.FromConfiguration(app.Configuration);

app.UseCors("public-api");

app.MapGet("/", () => Results.Ok(new ServiceInfoResponse("Survivalcraft Center Server", 1)));

app.MapGet("/health", (CenterState state) => {
    DateTimeOffset now = DateTimeOffset.UtcNow;
    state.PruneExpired(options, now);
    return Results.Ok(new HealthResponse(
        "ok",
        now,
        state.Relays.Count,
        state.Rooms.Count,
        options.RelayToken is not null,
        options.AdminToken is not null));
});

app.MapPost("/api/v1/relays/heartbeat", (RelayHeartbeatRequest request, HttpContext http, CenterState state) => {
    if (!IsAuthorized(http.Request, options.RelayToken, "X-Relay-Token")) return Results.Unauthorized();

    string? error = ValidateRelayHeartbeat(request);
    if (error is not null) return Results.BadRequest(new ErrorResponse(error));

    DateTimeOffset now = DateTimeOffset.UtcNow;
    string relayId = NormalizeRequired(request.RelayId);
    RelayEntry relay = new(
        relayId,
        request.PublicHost.Trim(),
        request.UdpPort,
        NormalizeOptional(request.Region),
        NormalizeOptional(request.Version),
        Math.Max(0, request.RoomCount ?? 0),
        Math.Max(0, request.PeerCount ?? 0),
        request.Priority ?? 0,
        request.Enabled ?? true,
        now);

    state.Relays[relayId] = relay;
    state.PruneExpired(options, now);
    return Results.Ok(new RelayHeartbeatResponse(relayId, relay.LastSeenUtc));
});

app.MapGet("/api/v1/relays/candidates", (CenterState state) => {
    DateTimeOffset now = DateTimeOffset.UtcNow;
    state.PruneExpired(options, now);
    RelayCandidateResponse[] relays = state.GetActiveRelays(options, now)
        .OrderBy(r => r.Priority)
        .ThenBy(r => r.RoomCount)
        .ThenBy(r => r.PeerCount)
        .ThenBy(r => r.RelayId, StringComparer.OrdinalIgnoreCase)
        .Select(r => new RelayCandidateResponse(
            r.RelayId,
            r.PublicHost,
            r.UdpPort,
            r.Region,
            r.Version,
            GetRelayStatus(r, options),
            r.Priority,
            r.RoomCount,
            r.PeerCount,
            (int)Math.Max(0, (now - r.LastSeenUtc).TotalSeconds)))
        .ToArray();

    return Results.Ok(new RelayCandidatesResponse(relays));
});

app.MapPost("/api/v1/rooms", (RoomRegistrationRequest request, HttpContext http, CenterState state) => {
    if (!IsAuthorized(http.Request, options.RelayToken, "X-Relay-Token")) return Results.Unauthorized();

    DateTimeOffset now = DateTimeOffset.UtcNow;
    state.PruneExpired(options, now);

    string? error = ValidateRoomRegistration(request);
    if (error is not null) return Results.BadRequest(new ErrorResponse(error));

    string relayId = NormalizeRequired(request.RelayId);
    if (!state.Relays.TryGetValue(relayId, out RelayEntry? relay) || !IsRelayActive(relay, options, now)) {
        return Results.BadRequest(new ErrorResponse("relay is not active"));
    }

    int ttlSeconds = ClampTtl(request.TtlSeconds, options);
    RoomEntry room = state.CreateRoom(
        options.CodeLength,
        relayId,
        NormalizeRequired(request.RelayRoomCode).ToUpperInvariant(),
        Math.Max(0, request.MaxPlayers ?? 0),
        now,
        now.AddSeconds(ttlSeconds));

    return Results.Ok(new RoomRegistrationResponse(
        room.GlobalCode,
        room.ExpiresAtUtc,
        options.RoomHeartbeatIntervalSeconds));
});

app.MapGet("/api/v1/rooms/{globalCode}", (string globalCode, CenterState state) => {
    DateTimeOffset now = DateTimeOffset.UtcNow;
    state.PruneExpired(options, now);

    string code = NormalizeCode(globalCode);
    if (!state.Rooms.TryGetValue(code, out RoomEntry? room) || room.ExpiresAtUtc <= now) {
        return Results.NotFound(new ErrorResponse("room not found"));
    }

    if (!state.Relays.TryGetValue(room.RelayId, out RelayEntry? relay) || !IsRelayActive(relay, options, now)) {
        return Results.NotFound(new ErrorResponse("relay not available"));
    }

    return Results.Ok(new RoomLookupResponse(
        room.GlobalCode,
        room.ExpiresAtUtc,
        [
            new RoomRelayResponse(
                relay.RelayId,
                relay.PublicHost,
                relay.UdpPort,
                room.RelayRoomCode,
                relay.Region,
                relay.Version,
                relay.Priority)
        ]));
});

app.MapPost("/api/v1/rooms/{globalCode}/heartbeat", async (string globalCode, HttpContext http, CenterState state) => {
    if (!IsAuthorized(http.Request, options.RelayToken, "X-Relay-Token")) return Results.Unauthorized();

    RoomHeartbeatRequest? request = null;
    if (http.Request.ContentLength.GetValueOrDefault() > 0) {
        request = await http.Request.ReadFromJsonAsync<RoomHeartbeatRequest>();
    }

    DateTimeOffset now = DateTimeOffset.UtcNow;
    state.PruneExpired(options, now);

    string code = NormalizeCode(globalCode);
    if (!state.Rooms.TryGetValue(code, out RoomEntry? room)) {
        return Results.NotFound(new ErrorResponse("room not found"));
    }

    if (!string.IsNullOrWhiteSpace(request?.RelayId)
        && !string.Equals(NormalizeRequired(request.RelayId), room.RelayId, StringComparison.OrdinalIgnoreCase)) {
        return Results.BadRequest(new ErrorResponse("relay does not own this room"));
    }

    int ttlSeconds = ClampTtl(request?.TtlSeconds, options);
    RoomEntry updated = room with { ExpiresAtUtc = now.AddSeconds(ttlSeconds) };
    state.Rooms[code] = updated;
    return Results.Ok(new RoomHeartbeatResponse(updated.GlobalCode, updated.ExpiresAtUtc));
});

app.MapDelete("/api/v1/rooms/{globalCode}", (string globalCode, HttpContext http, CenterState state) => {
    if (!IsAuthorized(http.Request, options.RelayToken, "X-Relay-Token")) return Results.Unauthorized();

    string code = NormalizeCode(globalCode);
    if (state.Rooms.TryRemove(code, out RoomEntry? room)) {
        state.RemoveRoomFromRelay(room.RelayId);
    }

    return Results.NoContent();
});

app.MapGet("/api/v1/relays", (HttpContext http, CenterState state) => {
    if (!IsAuthorized(http.Request, options.AdminToken, "X-Admin-Token")) return Results.Unauthorized();

    DateTimeOffset now = DateTimeOffset.UtcNow;
    state.PruneExpired(options, now);
    RelayAdminResponse[] relays = state.Relays.Values
        .OrderBy(r => r.RelayId, StringComparer.OrdinalIgnoreCase)
        .Select(r => new RelayAdminResponse(
            r.RelayId,
            r.PublicHost,
            r.UdpPort,
            r.Region,
            r.Version,
            r.Priority,
            r.Enabled,
            r.RoomCount,
            r.PeerCount,
            r.LastSeenUtc,
            (int)Math.Max(0, (now - r.LastSeenUtc).TotalSeconds),
            IsRelayActive(r, options, now),
            GetRelayStatus(r, options)))
        .ToArray();

    return Results.Ok(new RelayAdminListResponse(relays));
});

app.MapGet("/api/v1/rooms", (HttpContext http, CenterState state) => {
    if (!IsAuthorized(http.Request, options.AdminToken, "X-Admin-Token")) return Results.Unauthorized();

    DateTimeOffset now = DateTimeOffset.UtcNow;
    state.PruneExpired(options, now);
    RoomAdminResponse[] rooms = state.Rooms.Values
        .OrderBy(r => r.GlobalCode, StringComparer.OrdinalIgnoreCase)
        .Select(r => new RoomAdminResponse(
            r.GlobalCode,
            r.RelayId,
            r.RelayRoomCode,
            r.MaxPlayers,
            r.CreatedAtUtc,
            r.ExpiresAtUtc,
            (int)Math.Max(0, (r.ExpiresAtUtc - now).TotalSeconds)))
        .ToArray();

    return Results.Ok(new RoomAdminListResponse(rooms));
});

app.Run();

static bool IsAuthorized(HttpRequest request, string? expectedToken, string headerName) {
    if (string.IsNullOrEmpty(expectedToken)) return true;
    if (!request.Headers.TryGetValue(headerName, out var actualValues)) return false;

    string? actualToken = actualValues.FirstOrDefault();
    if (string.IsNullOrEmpty(actualToken)) return false;

    byte[] expectedBytes = Encoding.UTF8.GetBytes(expectedToken);
    byte[] actualBytes = Encoding.UTF8.GetBytes(actualToken);
    return expectedBytes.Length == actualBytes.Length
        && CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
}

static string? ValidateRelayHeartbeat(RelayHeartbeatRequest request) {
    if (string.IsNullOrWhiteSpace(request.RelayId)) return "relayId is required";
    if (string.IsNullOrWhiteSpace(request.PublicHost)) return "publicHost is required";
    if (request.UdpPort is <= 0 or > 65535) return "udpPort is invalid";
    return null;
}

static string? ValidateRoomRegistration(RoomRegistrationRequest request) {
    if (string.IsNullOrWhiteSpace(request.RelayId)) return "relayId is required";
    if (string.IsNullOrWhiteSpace(request.RelayRoomCode)) return "relayRoomCode is required";
    return null;
}

static string NormalizeRequired(string value) => value.Trim();

static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

static string NormalizeCode(string value) {
    StringBuilder builder = new();
    foreach (char c in value.Trim()) {
        if (c is '-' or ' ') continue;
        builder.Append(char.ToUpperInvariant(c));
    }
    return builder.ToString();
}

static int ClampTtl(int? requestedSeconds, CenterServerOptions options) {
    int seconds = requestedSeconds.GetValueOrDefault(options.RoomTtlSeconds);
    seconds = Math.Max(1, seconds);
    return Math.Min(seconds, options.MaxRoomTtlSeconds);
}

static bool IsRelayActive(RelayEntry relay, CenterServerOptions options, DateTimeOffset now) {
    return relay.Enabled && now - relay.LastSeenUtc <= TimeSpan.FromSeconds(options.RelayHeartbeatTimeoutSeconds);
}

static string GetRelayStatus(RelayEntry relay, CenterServerOptions options) {
    return relay.RoomCount >= options.RelayBusyRoomCount || relay.PeerCount >= options.RelayBusyPeerCount ? "Busy" : "Active";
}

public sealed class CenterState {
    private const string CodeAlphabet = "0123456789";
    private readonly ConcurrentDictionary<string, int> m_relayRoomCounts = new(StringComparer.OrdinalIgnoreCase);

    public ConcurrentDictionary<string, RelayEntry> Relays { get; } = new(StringComparer.OrdinalIgnoreCase);
    public ConcurrentDictionary<string, RoomEntry> Rooms { get; } = new(StringComparer.OrdinalIgnoreCase);

    public IEnumerable<RelayEntry> GetActiveRelays(CenterServerOptions options, DateTimeOffset now) {
        return Relays.Values.Where(r => r.Enabled && now - r.LastSeenUtc <= TimeSpan.FromSeconds(options.RelayHeartbeatTimeoutSeconds));
    }

    public RoomEntry CreateRoom(
        int codeLength,
        string relayId,
        string relayRoomCode,
        int maxPlayers,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc) {
        for (int attempt = 0; attempt < 64; attempt++) {
            string code = RandomCode(codeLength);
            RoomEntry room = new(code, relayId, relayRoomCode, maxPlayers, createdAtUtc, expiresAtUtc);
            if (Rooms.TryAdd(code, room)) {
                AddRoomToRelay(relayId);
                return room;
            }
        }
        throw new InvalidOperationException("Unable to create a unique global code.");
    }

    public void AddRoomToRelay(string relayId) {
        m_relayRoomCounts.AddOrUpdate(relayId, 1, (_, count) => count + 1);
        if (Relays.TryGetValue(relayId, out RelayEntry? relay)) {
            Relays[relayId] = relay with { RoomCount = m_relayRoomCounts[relayId] };
        }
    }

    public void RemoveRoomFromRelay(string relayId) {
        int count = m_relayRoomCounts.AddOrUpdate(relayId, 0, (_, current) => Math.Max(0, current - 1));
        if (Relays.TryGetValue(relayId, out RelayEntry? relay)) {
            Relays[relayId] = relay with { RoomCount = count };
        }
    }

    public void PruneExpired(CenterServerOptions options, DateTimeOffset now) {
        foreach ((string code, RoomEntry room) in Rooms) {
            if (room.ExpiresAtUtc <= now && Rooms.TryRemove(code, out RoomEntry? removed)) {
                RemoveRoomFromRelay(removed.RelayId);
            }
        }

        TimeSpan relayTimeout = TimeSpan.FromSeconds(options.RelayHeartbeatTimeoutSeconds * 2);
        foreach ((string relayId, RelayEntry relay) in Relays) {
            if (now - relay.LastSeenUtc > relayTimeout) {
                Relays.TryRemove(relayId, out _);
            }
        }
    }

    private static string RandomCode(int length) {
        Span<char> chars = stackalloc char[length];
        for (int i = 0; i < chars.Length; i++) {
            chars[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
        }
        return new string(chars);
    }
}

public sealed record CenterServerOptions(
    string? RelayToken,
    string? AdminToken,
    int RelayHeartbeatTimeoutSeconds,
    int RoomTtlSeconds,
    int MaxRoomTtlSeconds,
    int RoomHeartbeatIntervalSeconds,
    int RelayBusyRoomCount,
    int RelayBusyPeerCount,
    int CodeLength) {
    public static CenterServerOptions FromConfiguration(IConfiguration configuration) {
        IConfigurationSection section = configuration.GetSection("CenterServer");
        return new CenterServerOptions(
            EmptyToNull(section["RelayToken"]),
            EmptyToNull(section["AdminToken"]),
            ReadPositiveInt(section, "RelayHeartbeatTimeoutSeconds", 45),
            ReadPositiveInt(section, "RoomTtlSeconds", 120),
            ReadPositiveInt(section, "MaxRoomTtlSeconds", 7200),
            ReadPositiveInt(section, "RoomHeartbeatIntervalSeconds", 15),
            ReadPositiveInt(section, "RelayBusyRoomCount", 32),
            ReadPositiveInt(section, "RelayBusyPeerCount", 256),
            Math.Clamp(ReadPositiveInt(section, "CodeLength", 4), 4, 4));
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static int ReadPositiveInt(IConfiguration section, string key, int fallback) {
        return int.TryParse(section[key], out int value) && value > 0 ? value : fallback;
    }
}

public sealed record RelayEntry(
    string RelayId,
    string PublicHost,
    int UdpPort,
    string? Region,
    string? Version,
    int RoomCount,
    int PeerCount,
    int Priority,
    bool Enabled,
    DateTimeOffset LastSeenUtc);

public sealed record RoomEntry(
    string GlobalCode,
    string RelayId,
    string RelayRoomCode,
    int MaxPlayers,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc);

public sealed record RelayHeartbeatRequest(
    string RelayId,
    string PublicHost,
    int UdpPort,
    string? Region,
    string? Version,
    int? RoomCount,
    int? PeerCount,
    int? Priority,
    bool? Enabled);

public sealed record RoomRegistrationRequest(
    string RelayId,
    string RelayRoomCode,
    int? MaxPlayers,
    int? TtlSeconds);

public sealed record RoomHeartbeatRequest(string? RelayId, int? TtlSeconds);

public sealed record ServiceInfoResponse(string Name, int ApiVersion);

public sealed record HealthResponse(
    string Status,
    DateTimeOffset TimeUtc,
    int RelayCount,
    int RoomCount,
    bool RelayTokenConfigured,
    bool AdminTokenConfigured);

public sealed record ErrorResponse(string Error);

public sealed record RelayHeartbeatResponse(string RelayId, DateTimeOffset LastSeenUtc);

public sealed record RelayCandidateResponse(
    string RelayId,
    string Host,
    int UdpPort,
    string? Region,
    string? Version,
    string Status,
    int Priority,
    int RoomCount,
    int PeerCount,
    int LastSeenSecondsAgo);

public sealed record RelayCandidatesResponse(RelayCandidateResponse[] Relays);

public sealed record RoomRegistrationResponse(
    string GlobalCode,
    DateTimeOffset ExpiresAtUtc,
    int HeartbeatIntervalSeconds);

public sealed record RoomLookupResponse(
    string GlobalCode,
    DateTimeOffset ExpiresAtUtc,
    RoomRelayResponse[] Relays);

public sealed record RoomRelayResponse(
    string RelayId,
    string Host,
    int UdpPort,
    string RoomCode,
    string? Region,
    string? Version,
    int Priority);

public sealed record RoomHeartbeatResponse(string GlobalCode, DateTimeOffset ExpiresAtUtc);

public sealed record RelayAdminResponse(
    string RelayId,
    string PublicHost,
    int UdpPort,
    string? Region,
    string? Version,
    int Priority,
    bool Enabled,
    int RoomCount,
    int PeerCount,
    DateTimeOffset LastSeenUtc,
    int LastSeenSecondsAgo,
    bool Active,
    string Status);

public sealed record RelayAdminListResponse(RelayAdminResponse[] Relays);

public sealed record RoomAdminResponse(
    string GlobalCode,
    string RelayId,
    string RelayRoomCode,
    int MaxPlayers,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    int ExpiresInSeconds);

public sealed record RoomAdminListResponse(RoomAdminResponse[] Rooms);
