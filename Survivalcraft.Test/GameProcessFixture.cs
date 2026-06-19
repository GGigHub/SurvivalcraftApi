using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Survivalcraft.Test;

public sealed class GameProcessFixture : IDisposable {
    private readonly List<GameProcessContext> contexts = [];
    private readonly string runRootDirectory;

    public GameProcessContext Server { get; }
    public GameProcessContext Client { get; }

    public GameProcessFixture() {
        runRootDirectory = Path.Combine(
            Path.GetTempPath(),
            "Survivalcraft.Test",
            DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"));
        Server = CreateContext("Server");
        Client = CreateContext("Client");
        Server.Start();
    }

    public GameProcessContext CreateContext(string roleName) {
        GameProcessContext context = GameProcessContext.Create(roleName, runRootDirectory);
        contexts.Add(context);
        return context;
    }

    public async Task<bool> WaitLogAsync(string message, GameProcessContext target, int seconds = 5) {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(seconds));
        var (success, _) = await WaitLogAsync(target, message, timeout.Token);
        return success;
    }

    public async Task<(bool success, string matchedLog)> WaitLogAsync(GameProcessContext target, string message, CancellationToken cancellationToken) {
        while (!cancellationToken.IsCancellationRequested) {
            ThrowIfAnyContextHasError();

            foreach (string logMessage in target.ReceivedMessages) {
                if (logMessage.Contains(message)) {
                    target.ReceivedMessages.Clear();
                    return (true, logMessage);
                }
            }

            try {
                await Task.Delay(50, cancellationToken);
            }
            catch (OperationCanceledException) {
                break;
            }
        }

        return (false, string.Empty);
    }

    public void ThrowIfAnyContextHasError() {
        foreach (GameProcessContext context in contexts) {
            context.ThrowIfErrorLogReceived();
        }
    }

    public void Dispose() {
        for (int i = contexts.Count - 1; i >= 0; i--) {
            contexts[i].Dispose();
        }
    }
}
