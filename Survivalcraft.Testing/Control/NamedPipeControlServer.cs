using System.IO.Pipes;

namespace Survivalcraft.Testing.Control;

public sealed class NamedPipeControlServer : IAsyncDisposable {
    private readonly string pipeName;
    private readonly Func<ControlRequest, CancellationToken, ValueTask<ControlResponse>> handler;
    private readonly CancellationTokenSource disposeSource = new();
    private Task? runTask;
    private bool disposed;

    public NamedPipeControlServer(
        string pipeName,
        Func<ControlRequest, CancellationToken, ValueTask<ControlResponse>> handler) {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        this.pipeName = pipeName;
        this.handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    public Task StartAsync(CancellationToken cancellationToken = default) {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (runTask != null) {
            throw new InvalidOperationException(
                $"Control pipe server '{pipeName}' has already started.");
        }
        runTask = RunAsync(cancellationToken);
        return Task.CompletedTask;
    }

    private async Task RunAsync(CancellationToken externalToken) {
        using CancellationTokenSource linkedSource =
            CancellationTokenSource.CreateLinkedTokenSource(disposeSource.Token, externalToken);
        CancellationToken cancellationToken = linkedSource.Token;

        while (!cancellationToken.IsCancellationRequested) {
            await using NamedPipeServerStream server = new(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.WriteThrough);
            try {
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                while (server.IsConnected && !cancellationToken.IsCancellationRequested) {
                    ControlRequest? request = await LengthPrefixedJsonStream.ReadAsync<ControlRequest>(
                        server,
                        cancellationToken).ConfigureAwait(false);
                    if (request == null) {
                        break;
                    }

                    ControlResponse response;
                    if (request.Version != ControlProtocol.CurrentVersion) {
                        response = ControlProtocol.Failure(
                            request,
                            "version_mismatch",
                            $"Server protocol is {ControlProtocol.CurrentVersion}; request protocol is {request.Version}.");
                    }
                    else {
                        try {
                            response = await handler(request, cancellationToken).ConfigureAwait(false);
                        }
                        catch (Exception exception) {
                            response = ControlProtocol.Failure(
                                request,
                                "handler_error",
                                exception.Message,
                                exception.ToString());
                        }
                    }
                    await LengthPrefixedJsonStream.WriteAsync(
                        server,
                        response,
                        cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                break;
            }
            catch (IOException) when (!cancellationToken.IsCancellationRequested) {
                // A client can terminate between frames. Accept a new connection.
            }
        }
    }

    public async ValueTask DisposeAsync() {
        if (disposed) {
            return;
        }
        disposed = true;
        disposeSource.Cancel();
        if (runTask != null) {
            try {
                await runTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) {
                // Expected during shutdown.
            }
        }
        disposeSource.Dispose();
    }
}
