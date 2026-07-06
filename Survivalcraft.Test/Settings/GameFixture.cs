using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Silk.NET.OpenGLES;
using Xunit;

namespace Survivalcraft.Test;

public class GameFixture : IAsyncLifetime, IDisposable {
    private const int MaxConcurrentGameFixtures = 1;
    private static readonly SemaphoreSlim GameFixtureGate = new(MaxConcurrentGameFixtures);

    private readonly List<GameContext> contexts = [];
    private string runRootDirectory = string.Empty;
    private bool gateAcquired;
    private bool disposed;

    public GameContext Server { get; private set; } = null!;
    public GameContext Client { get; private set; } = null!;
    public GameContext Spectator { get; private set; } = null!;
    public CancellationTokenSource ErrorCts { get; } = new();

    public async Task InitializeAsync() {
        await GameFixtureGate.WaitAsync();
        gateAcquired = true;

        try {
            runRootDirectory = Path.Combine(
                Path.GetTempPath(),
                "Survivalcraft.Test",
                Guid.NewGuid().ToString("N")[..8]);
            Server = CreateContext("Server");
            Client = CreateContext("Client");
            Spectator = CreateContext("Spectator");
            Server.Start();
            Client.Start();
            Server.WaitLog("Entered screen \"MainMenu\"");
            Client.WaitLog("Entered screen \"MainMenu\"");
        }
        catch {
            Dispose();
            throw;
        }
    }

    public Task DisposeAsync() {
        Dispose();
        return Task.CompletedTask;
    }

    public GameContext CreateContext(string roleName) {
        GameContext context = GameContext.Create(roleName, runRootDirectory);
        context.OnFatalError += (ctx, ex) => {
            ErrorCts.Cancel();
        };
        contexts.Add(context);
        return context;
    }

    public void ThrowIfAnyContextHasError() {
        foreach (GameContext context in contexts) {
            if (context.FatalException != null) {
                throw context.FatalException;
            }
        }
    }

    public void Dispose() {
        if (disposed) {
            return;
        }
        disposed = true;

        for (int i = contexts.Count - 1; i >= 0; i--) {
            contexts[i].Dispose();
        }
        ErrorCts.Dispose();
        if (gateAcquired) {
            gateAcquired = false;
            GameFixtureGate.Release();
        }
    }
}
