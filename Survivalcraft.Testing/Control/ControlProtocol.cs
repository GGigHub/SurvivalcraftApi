using System.Text.Json;
using System.Text.Json.Serialization;
using Survivalcraft.Testing.Networking;
using Survivalcraft.Testing.Snapshots;

namespace Survivalcraft.Testing.Control;

public static class ControlProtocol {
    public const int CurrentVersion = 1;
    public const int MaximumFrameBytes = 4 * 1024 * 1024;

    public static JsonSerializerOptions JsonOptions { get; } = CreateJsonOptions();

    public static ControlRequest CreateRequest<TPayload>(string command, TPayload payload, string? requestId = null) {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        return new ControlRequest(
            requestId ?? Guid.NewGuid().ToString("N"),
            CurrentVersion,
            command,
            JsonSerializer.SerializeToElement(payload, JsonOptions));
    }

    public static ControlResponse Success<T>(
        ControlRequest request,
        T result,
        long sequence = 0) =>
        new(
            request.Id,
            CurrentVersion,
            true,
            JsonSerializer.SerializeToElement(result, JsonOptions),
            null,
            sequence);

    public static ControlResponse Failure(
        ControlRequest request,
        string code,
        string message,
        string? details = null,
        long sequence = 0) =>
        new(
            request.Id,
            CurrentVersion,
            false,
            null,
            new ControlError(code, message, details),
            sequence);

    public static TPayload DeserializePayload<TPayload>(this ControlRequest request) =>
        request.Payload.Deserialize<TPayload>(JsonOptions)
        ?? throw new ControlProtocolException(
            "invalid_payload",
            $"Command '{request.Command}' did not contain a valid {typeof(TPayload).Name} payload.");

    public static TResult DeserializeResult<TResult>(this ControlResponse response) {
        if (!response.Success) {
            ControlError error = response.Error
                ?? new ControlError("unknown_error", "The control command failed without error details.");
            throw new ControlCommandException(error.Code, error.Message, error.Details);
        }
        if (!response.Result.HasValue) {
            throw new ControlProtocolException(
                "missing_result",
                $"Response '{response.Id}' did not contain a result.");
        }
        return response.Result.Value.Deserialize<TResult>(JsonOptions)
            ?? throw new ControlProtocolException(
                "invalid_result",
                $"Response '{response.Id}' did not contain a valid {typeof(TResult).Name} result.");
    }

    private static JsonSerializerOptions CreateJsonOptions() {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web) {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}

public sealed record ControlRequest(
    string Id,
    int Version,
    string Command,
    JsonElement Payload);

public sealed record ControlResponse(
    string Id,
    int Version,
    bool Success,
    JsonElement? Result,
    ControlError? Error,
    long Sequence);

public sealed record ControlError(string Code, string Message, string? Details = null);

public sealed class ControlProtocolException : Exception {
    public ControlProtocolException(string code, string message)
        : base(message) {
        Code = code;
    }

    public string Code { get; }
}

public sealed class ControlCommandException : Exception {
    public ControlCommandException(string code, string message, string? details = null)
        : base(details is null ? message : $"{message}{Environment.NewLine}{details}") {
        Code = code;
        Details = details;
    }

    public string Code { get; }
    public string? Details { get; }
}

public static class ControlCommandNames {
    public const string Ping = "system.ping";
    public const string Describe = "system.describe";
    public const string GetStatus = "node.getStatus";
    public const string ConfigureNode = "node.configure";
    public const string CreateWorld = "world.create";
    public const string ReloadWorld = "world.reload";
    public const string SaveWorld = "world.save";
    public const string ConnectClient = "network.connect";
    public const string StartRelayHost = "network.startRelayHost";
    public const string ConnectRelay = "network.connectRelay";
    public const string GetRelayStatus = "network.getRelayStatus";
    public const string EnterWorld = "player.enterWorld";
    public const string Disconnect = "network.disconnect";
    public const string ChangeTerrainCell = "terrain.changeCell";
    public const string GetSnapshot = "state.getSnapshot";
    public const string GetSnapshotV2 = "state.getSnapshotV2";
    public const string GetEvents = "state.getEvents";
    public const string ResetEvents = "state.resetEvents";
    public const string SetCreativeFly = "player.setCreativeFly";
    public const string PreparePlayerMotionFixture = "fixture.player.prepareMotion";
    public const string ApplyPlayerMotionFixture = "fixture.player.applyMotion";
    public const string PlayerMove = "player.move";
    public const string PlayerLook = "player.look";
    public const string PlayerSetCrouch = "player.setCrouch";
    public const string PrepareTerrainInteractionFixture = "fixture.player.prepareTerrainInteraction";
    public const string ApplyTerrainInteractionFixture = "fixture.player.applyTerrainInteraction";
    public const string PlayerUse = "player.use";
    public const string PlayerPlace = "player.place";
    public const string PlayerDig = "player.dig";
    public const string PrepareTerrainStreamingFixture = "fixture.terrain.prepareRegion";
    public const string ApplyTerrainStreamingView = "fixture.terrain.applyView";
    public const string RunTerrainRevisionBurst = "terrain.runRevisionBurst";
    public const string SetClock = "time.setClock";
    public const string AdvanceTicks = "time.advanceTicks";
    public const string SetNetworkFaults = "network.setFaults";
    public const string ConfigureCompatibility = "network.configureCompatibility";
    public const string GetCompatibility = "network.getCompatibility";
    public const string ConfigureModChannels = "network.configureModChannels";
    public const string GetModChannels = "network.getModChannels";
    public const string SendTestModMessage = "network.sendTestModMessage";
    public const string GetTestModMessages = "network.getTestModMessages";
    public const string ResetTestModMessages = "network.resetTestModMessages";
    public const string ClickWidget = "ui.clickWidget";
    public const string ExecuteLegacyScript = "legacy.executeScript";
}

public sealed record EmptyCommand;
public sealed record ControlAck(bool Accepted, string? Message = null);
public sealed record PingCommand(string? Message = null);
public sealed record PingResult(
    string Message,
    string Role,
    int ProcessId,
    int ProtocolVersion,
    DateTimeOffset Timestamp);

public sealed record ConfigureNodeCommand(
    string Role,
    int Seed,
    bool FixedRandom = true,
    bool DisableAudio = true,
    bool DisableMultithreadedTerrain = true);

public sealed record CreateWorldCommand(
    string Name,
    string Seed,
    bool Creative = true,
    bool FlatTerrain = true);

public sealed record CreateWorldResult(
    string DirectoryName,
    string Name,
    int WorldSeed);

public sealed record ReloadWorldCommand(string DirectoryName);
public sealed record SaveWorldCommand(bool WaitForCompletion = true);
public sealed record SaveWorldResult(string DirectoryName, bool Saved);

public sealed record ConnectClientCommand(string Host, int Port);
public sealed record StartRelayHostCommand(string Host, int Port);
public sealed record ConnectRelayCommand(string Host, int Port, string RoomCode);
public sealed record RelayStatus(
    string Mode,
    bool HostConnected,
    bool ClientConnected,
    string HostCodeStatus,
    string RoomCode,
    string GlobalCode,
    int HostSessionCount);
public sealed record EnterWorldCommand(string PlayerName);
public sealed record DisconnectCommand(
    bool SaveWorld = false,
    bool ReturnToMainMenu = true);

public enum TestBlockKind {
    Air,
    Dirt,
    Glass
}

public sealed record ChangeTerrainCellCommand(
    int? X = null,
    int? Y = null,
    int? Z = null,
    TestBlockKind Block = TestBlockKind.Glass,
    int Data = 0);

public sealed record TerrainMutationResult(
    TerrainCellState Cell,
    int PreviousValue);

public sealed record GetSnapshotCommand(
    TerrainCellCoordinate[]? TerrainCells = null,
    bool IncludeEntities = true);

public sealed record SetClockCommand(
    bool Manual,
    double InitialSeconds = 0);

public sealed record AdvanceTicksCommand(
    int Ticks,
    double SecondsPerTick = 0.05);

public sealed record ClockState(
    bool Manual,
    long FrameIndex,
    double TimeSeconds);

public sealed record SetNetworkFaultsCommand(NetworkFaultProfile Profile);
public sealed record NetworkFaultState(
    NetworkFaultProfile Profile,
    long Sent,
    long Dropped,
    long Duplicated,
    int Pending);

public sealed record CompatibilityModDescriptor(
    string PackageName,
    string Version,
    string ApiVersion);

public sealed record ConfigureCompatibilityCommand(
    bool ClearOverride = false,
    int? ProtocolRevision = null,
    string? ApiVersion = null,
    int? Capabilities = null,
    CompatibilityModDescriptor[]? Mods = null);

public sealed record CompatibilityIssueState(
    string Code,
    string PackageName,
    string Expected,
    string Actual,
    string Message);

public sealed record CompatibilityStatus(
    string State,
    bool OverrideActive,
    int ProtocolRevision,
    string ApiVersion,
    int Capabilities,
    CompatibilityModDescriptor[] Mods,
    bool? Accepted,
    string? PrimaryCode,
    string? Summary,
    CompatibilityIssueState[] Issues);

public sealed record ModChannelDescriptorState(
    string PackageName,
    string ChannelName,
    int Version,
    int Directions,
    int MaximumPayloadBytes);

public sealed record ConfigureModChannelsCommand(
    bool ClearOverride = false,
    ModChannelDescriptorState[]? Channels = null);

public sealed record ModNetworkMetricsState(
    long Sent,
    long Received,
    long Delivered,
    long Dropped,
    long Duplicates,
    long RateLimited,
    long HandlerErrors);

public sealed record ModNetworkDispatchState(
    DateTimeOffset Timestamp,
    string PackageName,
    string ChannelName,
    string MessageId,
    string Code,
    bool IsServer,
    int PayloadBytes,
    string Detail);

public sealed record ModChannelStatus(
    bool OverrideActive,
    string Fingerprint,
    ModChannelDescriptorState[] Channels,
    ModNetworkMetricsState Metrics,
    ModNetworkDispatchState? LastDispatch);

public sealed record SendTestModMessageCommand(
    string Payload,
    bool Broadcast = false,
    bool FailHandler = false,
    string? CorrelationId = null);

public sealed record TestModMessageState(
    DateTimeOffset Timestamp,
    string CorrelationId,
    string Payload,
    string MessageId,
    bool ReceivedOnServer,
    int? SenderPlayerIndex,
    bool EchoRequested,
    bool HandlerFailureRequested);

public sealed record TestModSendResult(
    string CorrelationId,
    string Route,
    int RecipientCount,
    int PayloadBytes);

public sealed record TestModMessagesStatus(
    TestModMessageState[] Messages,
    ModChannelStatus Channel);

public sealed record ClickWidgetCommand(string NameOrText);
public sealed record ExecuteLegacyScriptCommand(string Code, bool ReturnValue);

public sealed record NodeStatus(
    string Role,
    string Screen,
    bool ContentLoaded,
    bool ProjectLoaded,
    bool IsServerRunning,
    bool IsClientRunning,
    string NetworkState,
    int PlayerIndex,
    int PlayerCount,
    int ServerSessionCount,
    bool MainPlayerReady,
    long FrameIndex,
    double TimeSeconds,
    NetworkFaultState NetworkFaults,
    string CompatibilityState,
    string? CompatibilityCode);
