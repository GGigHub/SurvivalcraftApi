using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Survivalcraft.Test {
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

        public void Dispose() {
            for (int i = contexts.Count - 1; i >= 0; i--) {
                contexts[i].Dispose();
            }
        }
    }
}
