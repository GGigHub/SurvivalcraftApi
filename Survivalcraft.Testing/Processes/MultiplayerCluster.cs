using Survivalcraft.Testing.Control;

namespace Survivalcraft.Testing.Processes;

public enum MultiplayerTransportKind {
    Direct,
    Relay
}

public sealed record MultiplayerClusterOptions(
    string RuntimeDirectory,
    string WorkDirectory,
    MultiplayerTransportKind Transport,
    int Seed,
    int ClientCount = 2,
    TimeSpan? StartupTimeout = null);

public interface IMultiplayerTransportHarness {
    MultiplayerTransportKind Kind { get; }

    Task<string?> StartHostAsync(
        GameNodeProcess server,
        RelayServerProcess? relay,
        TimeSpan timeout,
        CancellationToken cancellationToken);

    Task ConnectClientAsync(
        GameNodeProcess server,
        GameNodeProcess client,
        RelayServerProcess? relay,
        string? roomCode,
        CancellationToken cancellationToken);
}

public sealed class MultiplayerCluster : IAsyncDisposable {
    private readonly MultiplayerClusterOptions options;
    private readonly IMultiplayerTransportHarness transportHarness;
    private bool started;
    private bool disposed;

    public MultiplayerCluster(MultiplayerClusterOptions options) {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        ArgumentException.ThrowIfNullOrWhiteSpace(options.RuntimeDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.WorkDirectory);
        if (options.ClientCount is < 1 or > 8) {
            throw new ArgumentOutOfRangeException(
                nameof(options.ClientCount),
                "A multiplayer cluster supports between one and eight clients.");
        }

        RuntimeDirectory = Path.GetFullPath(options.RuntimeDirectory);
        WorkDirectory = Path.GetFullPath(options.WorkDirectory);
        TimeSpan startupTimeout = options.StartupTimeout ?? TimeSpan.FromSeconds(90);
        if (startupTimeout <= TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(options.StartupTimeout));
        }

        Server = new GameNodeProcess(new GameNodeOptions(
            "server",
            RuntimeDirectory,
            Path.Combine(WorkDirectory, "server"),
            StartupTimeout: startupTimeout));
        Clients = Enumerable.Range(0, options.ClientCount)
            .Select(index => new GameNodeProcess(new GameNodeOptions(
                $"client-{(char)('a' + index)}",
                RuntimeDirectory,
                Path.Combine(WorkDirectory, $"client-{(char)('a' + index)}"),
                StartupTimeout: startupTimeout)))
            .ToArray();
        if (options.Transport == MultiplayerTransportKind.Relay) {
            Relay = new RelayServerProcess(new RelayServerOptions(
                RuntimeDirectory,
                StartupTimeout: startupTimeout));
            transportHarness = new RelayTransportHarness();
        }
        else {
            transportHarness = new DirectTransportHarness();
        }
    }

    public string RuntimeDirectory { get; }
    public string WorkDirectory { get; }
    public MultiplayerTransportKind Transport => transportHarness.Kind;
    public GameNodeProcess Server { get; }
    public GameNodeProcess[] Clients { get; }
    public RelayServerProcess? Relay { get; }
    public string? RoomCode { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken = default) {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (started) {
            throw new InvalidOperationException("The multiplayer cluster has already started.");
        }
        started = true;
        Directory.CreateDirectory(WorkDirectory);
        try {
            if (Relay != null) {
                await Relay.StartAsync(cancellationToken).ConfigureAwait(false);
            }
            await Task.WhenAll(
                new[] { Server }
                    .Concat(Clients)
                    .Select(node => node.StartAsync(cancellationToken)))
                .ConfigureAwait(false);
        }
        catch {
            await DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task ConfigureDeterminismAsync(
        CancellationToken cancellationToken = default) {
        EnsureStarted();
        await Task.WhenAll(
            new[] { Server }
                .Concat(Clients)
                .Select(node => node.SendAsync<ConfigureNodeCommand, ControlAck>(
                    ControlCommandNames.ConfigureNode,
                    new ConfigureNodeCommand(node.Role, options.Seed),
                    cancellationToken: cancellationToken)))
            .ConfigureAwait(false);
    }

    public async Task<string?> StartHostAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default) {
        EnsureStarted();
        RoomCode = await transportHarness.StartHostAsync(
            Server,
            Relay,
            timeout,
            cancellationToken).ConfigureAwait(false);
        return RoomCode;
    }

    public Task ConnectClientAsync(
        GameNodeProcess client,
        CancellationToken cancellationToken = default) {
        EnsureStarted();
        if (!Clients.Contains(client)) {
            throw new ArgumentException("Client does not belong to this cluster.", nameof(client));
        }
        return transportHarness.ConnectClientAsync(
            Server,
            client,
            Relay,
            RoomCode,
            cancellationToken);
    }

    private void EnsureStarted() {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!started) {
            throw new InvalidOperationException("The multiplayer cluster has not started.");
        }
    }

    public async ValueTask DisposeAsync() {
        if (disposed) {
            return;
        }
        disposed = true;
        foreach (GameNodeProcess client in Clients.Reverse()) {
            await client.DisposeAsync().ConfigureAwait(false);
        }
        await Server.DisposeAsync().ConfigureAwait(false);
        if (Relay != null) {
            await Relay.DisposeAsync().ConfigureAwait(false);
        }
    }

    private sealed class DirectTransportHarness : IMultiplayerTransportHarness {
        public MultiplayerTransportKind Kind => MultiplayerTransportKind.Direct;

        public Task<string?> StartHostAsync(
            GameNodeProcess server,
            RelayServerProcess? relay,
            TimeSpan timeout,
            CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);

        public async Task ConnectClientAsync(
            GameNodeProcess server,
            GameNodeProcess client,
            RelayServerProcess? relay,
            string? roomCode,
            CancellationToken cancellationToken) {
            _ = await client.SendAsync<ConnectClientCommand, ControlAck>(
                ControlCommandNames.ConnectClient,
                new ConnectClientCommand("127.0.0.1", server.GamePort),
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class RelayTransportHarness : IMultiplayerTransportHarness {
        public MultiplayerTransportKind Kind => MultiplayerTransportKind.Relay;

        public async Task<string?> StartHostAsync(
            GameNodeProcess server,
            RelayServerProcess? relay,
            TimeSpan timeout,
            CancellationToken cancellationToken) {
            RelayServerProcess requiredRelay = relay
                ?? throw new InvalidOperationException("Relay transport requires a relay process.");
            _ = await server.SendAsync<StartRelayHostCommand, ControlAck>(
                ControlCommandNames.StartRelayHost,
                new StartRelayHostCommand("127.0.0.1", requiredRelay.Port),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
            RelayStatus? last = null;
            while (DateTimeOffset.UtcNow < deadline) {
                cancellationToken.ThrowIfCancellationRequested();
                last = await server.SendAsync<EmptyCommand, RelayStatus>(
                    ControlCommandNames.GetRelayStatus,
                    new EmptyCommand(),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                if (last.HostConnected && !string.IsNullOrWhiteSpace(last.RoomCode)) {
                    return last.RoomCode;
                }
                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken)
                    .ConfigureAwait(false);
            }
            throw new TimeoutException(
                $"Relay host did not receive a room code. Last={last}.");
        }

        public async Task ConnectClientAsync(
            GameNodeProcess server,
            GameNodeProcess client,
            RelayServerProcess? relay,
            string? roomCode,
            CancellationToken cancellationToken) {
            RelayServerProcess requiredRelay = relay
                ?? throw new InvalidOperationException("Relay transport requires a relay process.");
            if (string.IsNullOrWhiteSpace(roomCode)) {
                throw new InvalidOperationException("Relay room has not been started.");
            }
            _ = await client.SendAsync<ConnectRelayCommand, ControlAck>(
                ControlCommandNames.ConnectRelay,
                new ConnectRelayCommand("127.0.0.1", requiredRelay.Port, roomCode),
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }
}
