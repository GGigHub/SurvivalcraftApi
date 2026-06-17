using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Silk.NET.GLFW;
using Silk.NET.OpenGLES;

namespace Survivalcraft.Test {
    public sealed class GameProcessFixture : IDisposable {
        private Process gameProcess;
        private UdpClient logServer;
        private CancellationTokenSource cts;

        public ConcurrentQueue<string> ReceivedMessages { get; set; } = new();

        public GameProcessFixture() {
            // Initialize UdpClient before starting games
            logServer = new UdpClient(23942);
            cts = new CancellationTokenSource();

            Task.Run(async () => await StartGameProcess(), cts.Token);
            Task.Run(async () => await ReviceMessage(), cts.Token);
        }

        private async Task StartGameProcess() {
            string exePath = Path.Combine(AppContext.BaseDirectory, "Survivalcraft.exe");
            if (!File.Exists(exePath)) {
                throw new FileNotFoundException($"Game executable not found at {exePath}");
            }

            var processInfo = new ProcessStartInfo {
                FileName = exePath,
                UseShellExecute = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                CreateNoWindow = true
            };

            gameProcess = Process.Start(processInfo) ?? throw new Exception("Failed to start the game process.");
        }

        public async Task ReviceMessage() {
            while (!cts.IsCancellationRequested) {
                try {
                    if (logServer.Available > 0) {
                        var receiveResult = await logServer.ReceiveAsync(cts.Token);
                        string message = Encoding.UTF8.GetString(receiveResult.Buffer);
                        ReceivedMessages.Enqueue(message);
                    }
                    else {
                        await Task.Delay(50, cts.Token);
                    }
                }
                catch (OperationCanceledException) {
                    break;
                }
                catch {
                    // Ignore other errors during shutdown
                }
            }
        }

        public async Task SendCommandAsync(string command) {
            byte[] cmdBytes = Encoding.UTF8.GetBytes(command);
            var endpoint = new IPEndPoint(IPAddress.Loopback, 23944);
            await logServer.SendAsync(cmdBytes, cmdBytes.Length, endpoint);
        }

        public async Task<bool> WaitLogAsync(string message, int seconds = 1) {
            CancellationTokenSource cts = new(TimeSpan.FromSeconds(seconds));
            return await WaitLogAsync(message, cts.Token);
        }

        public async Task<bool> WaitLogAsync(string message, CancellationToken cts) {
            while (!cts.IsCancellationRequested) {
                foreach (var msg in ReceivedMessages) {
                    if (msg.Contains("Error")) {
                        throw new Exception(msg);
                    }
                    if (msg.Contains(message)) {
                        return true;
                    }
                }
                try {
                    await Task.Delay(50, cts);
                }
                catch (OperationCanceledException) {
                    break;
                }
            }
            return false;
        }

        public void Dispose() {
            cts.Cancel();
            logServer?.Dispose();

            if (gameProcess != null && !gameProcess.HasExited) {
                gameProcess.Kill(true);
            }

            string docPath = Path.Combine(AppContext.BaseDirectory, "doc");
            if (Directory.Exists(docPath)) {
                Directory.Delete(docPath, true);
            }
        }
    }
}
