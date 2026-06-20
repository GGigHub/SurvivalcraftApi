using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Silk.NET.OpenGLES;

namespace Survivalcraft.Test;

public sealed class GameProcessFixture : IDisposable {
    private readonly List<GameProcessContext> contexts = [];
    private readonly string runRootDirectory;

    public GameProcessContext Server { get; }
    public GameProcessContext Client { get; }
    public CancellationTokenSource ErrorCts { get; } = new();

    public GameProcessFixture() {
        runRootDirectory = Path.Combine(
            Path.GetTempPath(),
            "Survivalcraft.Test",
            Guid.NewGuid().ToString("N")[..8]);
        Server = CreateContext("Server");
        Client = CreateContext("Client");
        Server.Start();
    }

    public GameProcessContext CreateContext(string roleName) {
        GameProcessContext context = GameProcessContext.Create(roleName, runRootDirectory);
        context.OnFatalError += (ctx, ex) => {
            ErrorCts.Cancel();
        };
        contexts.Add(context);
        return context;
    }

    public async Task<bool> WaitLogAsync(string message, GameProcessContext target, int seconds = 15) {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(seconds));
        var (success, _) = await WaitLogAsync(target, message, timeout.Token);
        return success;
    }

    public async Task<(bool success, string matchedLog)> WaitLogAsync(GameProcessContext target, string message, CancellationToken cancellationToken) {
        try {
            await foreach (string logMessage in target.LogChannel.Reader.ReadAllAsync(cancellationToken)) {
                if (logMessage.Contains(message)) {
                    return (true, logMessage);
                }
            }
        }
        // 违反了单一职责原则，我没想出更好的解决方案
        catch (OperationCanceledException) {
            if (ErrorCts.IsCancellationRequested) {
                ThrowIfAnyContextHasError();
            }
        }

        return (false, string.Empty);
    }

    public void ThrowIfAnyContextHasError() {
        foreach (GameProcessContext context in contexts) {
            if (context.FatalException != null) {
                throw context.FatalException;
            }
        }
    }

    public void Dispose() {
        for (int i = contexts.Count - 1; i >= 0; i--) {
            contexts[i].Dispose();
        }
        ErrorCts.Dispose();
    }
}
