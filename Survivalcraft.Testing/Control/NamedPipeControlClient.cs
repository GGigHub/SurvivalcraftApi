using System.IO.Pipes;

namespace Survivalcraft.Testing.Control;

public sealed class NamedPipeControlClient : IAsyncDisposable {
    private readonly string serverName;
    private readonly string pipeName;
    private readonly SemaphoreSlim roundTripGate = new(1, 1);
    private NamedPipeClientStream? stream;
    private bool disposed;

    public NamedPipeControlClient(string pipeName, string serverName = ".") {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        this.pipeName = pipeName;
        this.serverName = serverName;
    }

    public bool IsConnected => stream?.IsConnected == true;

    public async Task ConnectAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default) {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (IsConnected) {
            return;
        }

        stream?.Dispose();
        stream = new NamedPipeClientStream(
            serverName,
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.WriteThrough);

        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try {
            await stream.ConnectAsync(timeoutSource.Token).ConfigureAwait(false);
            stream.ReadMode = PipeTransmissionMode.Byte;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            stream.Dispose();
            stream = null;
            throw new TimeoutException(
                $"Timed out connecting to control pipe '{pipeName}' after {timeout}.");
        }
    }

    public async Task<TResult> SendAsync<TPayload, TResult>(
        string command,
        TPayload payload,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) {
        ObjectDisposedException.ThrowIf(disposed, this);
        NamedPipeClientStream connectedStream = stream is { IsConnected: true }
            ? stream
            : throw new InvalidOperationException(
                $"Control pipe '{pipeName}' is not connected.");

        await roundTripGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            ControlRequest request = ControlProtocol.CreateRequest(command, payload);
            using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout ?? TimeSpan.FromSeconds(30));

            try {
                await LengthPrefixedJsonStream.WriteAsync(
                    connectedStream,
                    request,
                    timeoutSource.Token).ConfigureAwait(false);
                ControlResponse response = await LengthPrefixedJsonStream.ReadAsync<ControlResponse>(
                    connectedStream,
                    timeoutSource.Token).ConfigureAwait(false)
                    ?? throw new EndOfStreamException(
                        $"Control pipe '{pipeName}' closed before response '{request.Id}'.");

                if (!string.Equals(response.Id, request.Id, StringComparison.Ordinal)) {
                    throw new ControlProtocolException(
                        "correlation_mismatch",
                        $"Expected response '{request.Id}', received '{response.Id}'.");
                }
                if (response.Version != ControlProtocol.CurrentVersion) {
                    throw new ControlProtocolException(
                        "version_mismatch",
                        $"Expected protocol {ControlProtocol.CurrentVersion}, received {response.Version}.");
                }
                return response.DeserializeResult<TResult>();
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
                throw new TimeoutException(
                    $"Control command '{command}' timed out on pipe '{pipeName}'.");
            }
        }
        finally {
            roundTripGate.Release();
        }
    }

    public async ValueTask DisposeAsync() {
        if (disposed) {
            return;
        }
        disposed = true;
        if (stream != null) {
            await stream.DisposeAsync().ConfigureAwait(false);
        }
        roundTripGate.Dispose();
    }
}
