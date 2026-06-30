using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Silk.NET.OpenGLES;

namespace Survivalcraft.Test;

public class GameFixture : IDisposable {
    private readonly List<GameContext> contexts = [];
    private readonly string runRootDirectory;

    public GameContext Server { get; }
    public GameContext Client { get; }
    public GameContext Spectator { get; }
    public CancellationTokenSource ErrorCts { get; } = new();

    public GameFixture() {
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
        for (int i = contexts.Count - 1; i >= 0; i--) {
            contexts[i].Dispose();
        }
        ErrorCts.Dispose();
    }
}