using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Survivalcraft.Test;

public sealed class GameProcessContext : IDisposable {
    private static readonly string[] RuntimeDirectoryNames = [
        "Bugs",
        "doc",
    ];

    private readonly UdpClient commandClient = new();
    private readonly Task receiveTask;

    private Process? gameProcess;
    private bool disposed;

    public GameProcessContext(string roleName, string sourceDirectory, string runDirectory) {
        RoleName = roleName;
        SourceDirectory = sourceDirectory;
        RunDirectory = runDirectory;
        Cts = new CancellationTokenSource();
        LogServer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        LogPort = ((IPEndPoint)LogServer.Client.LocalEndPoint!).Port;
        CmdPort = GetAvailableUdpPort();
        receiveTask = Task.Run(ReceiveMessagesAsync);
    }

    public string RoleName { get; }
    public string SourceDirectory { get; }
    public string RunDirectory { get; }
    public CancellationTokenSource Cts { get; }
    public int LogPort { get; }
    public int CmdPort { get; }
    public UdpClient LogServer { get; }
    public ConcurrentQueue<string> ReceivedMessages { get; } = new();
    public bool IsStarted => gameProcess != null;

    public static GameProcessContext Create(string roleName, string? runRootDirectory = null) {
        string sourceDirectory = AppContext.BaseDirectory;
        string runId = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
        string rootDirectory = runRootDirectory
            ?? Path.Combine(Path.GetTempPath(), "Survivalcraft.Test", runId);
        string runDirectory = Path.Combine(rootDirectory, SanitizePathSegment(roleName));
        return new GameProcessContext(roleName, sourceDirectory, runDirectory);
    }

    public void Start(params string[] arguments) {
        if (IsStarted) {
            throw new InvalidOperationException($"The {RoleName} game process has already been started.");
        }

        CopyRuntimeDirectory(SourceDirectory, RunDirectory);

        string exePath = Path.Combine(RunDirectory, "Survivalcraft.exe");
        if (!File.Exists(exePath)) {
            throw new FileNotFoundException($"Game executable not found at {exePath}");
        }

        var processInfo = new ProcessStartInfo {
            FileName = exePath,
            WorkingDirectory = RunDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            CreateNoWindow = true
        };
        foreach (string argument in BuildProcessArguments(arguments)) {
            processInfo.ArgumentList.Add(argument);
        }

        gameProcess = Process.Start(processInfo)
            ?? throw new InvalidOperationException($"Failed to start the {RoleName} game process.");
    }

    public async Task PushCommandAsync(string command) {
        byte[] cmdBytes = Encoding.UTF8.GetBytes(command);
        var endpoint = new IPEndPoint(IPAddress.Loopback, CmdPort);
        await commandClient.SendAsync(cmdBytes, endpoint);
    }

    public void ThrowIfErrorLogReceived() {
        if (TryGetErrorLog(out string? message)) {
            throw new InvalidOperationException($"[{RoleName}] {message}{Environment.NewLine}{GetRecentLogText()}");
        }
    }

    public bool TryGetErrorLog(out string? message) {
        foreach (string logMessage in ReceivedMessages) {
            if (IsErrorLog(logMessage)) {
                message = logMessage;
                return true;
            }
        }

        message = null;
        return false;
    }

    public string GetRecentLogText(int maxLines = 80) {
        string[] messages = ReceivedMessages.ToArray();
        int start = Math.Max(0, messages.Length - maxLines);
        return string.Join(string.Empty, messages[start..]);
    }

    public void Dispose() {
        if (disposed) {
            return;
        }
        disposed = true;

        Cts.Cancel();
        LogServer.Dispose();
        commandClient.Dispose();

        if (gameProcess != null && !gameProcess.HasExited) {
            gameProcess.Kill(true);
            gameProcess.WaitForExit(5000);
        }
        gameProcess?.Dispose();

        try {
            receiveTask.Wait(TimeSpan.FromSeconds(1));
        }
        catch {
            // ignored during shutdown
        }

        Cts.Dispose();
        DeleteRunDirectory();
    }

    private async Task ReceiveMessagesAsync() {
        while (!Cts.IsCancellationRequested) {
            try {
                UdpReceiveResult receiveResult = await LogServer.ReceiveAsync(Cts.Token);
                string message = Encoding.UTF8.GetString(receiveResult.Buffer);
                ReceivedMessages.Enqueue(message);
            }
            catch (OperationCanceledException) {
                break;
            }
            catch (ObjectDisposedException) {
                break;
            }
            catch {
                // Ignore transient UDP errors while the process is shutting down.
            }
        }
    }

    private string[] BuildProcessArguments(string[] arguments) => [
        "--log-port",
        LogPort.ToString(),
        "--cmd-port",
        CmdPort.ToString(),
        .. arguments
    ];

    private static bool IsErrorLog(string message) =>
        message.Contains("] [Error]", StringComparison.OrdinalIgnoreCase)
        || message.Contains("Unhandled exception", StringComparison.OrdinalIgnoreCase);

    private static void CopyRuntimeDirectory(string sourceDirectory, string destinationDirectory) {
        Directory.CreateDirectory(destinationDirectory);

        foreach (string directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories)) {
            string relativePath = Path.GetRelativePath(sourceDirectory, directory);
            if (ShouldSkipDirectory(relativePath)) {
                continue;
            }
            Directory.CreateDirectory(Path.Combine(destinationDirectory, relativePath));
        }

        foreach (string file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories)) {
            string relativePath = Path.GetRelativePath(sourceDirectory, file);
            if (ShouldSkipDirectory(Path.GetDirectoryName(relativePath) ?? string.Empty)) {
                continue;
            }

            string destinationFile = Path.Combine(destinationDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
            File.Copy(file, destinationFile, true);
        }
    }

    private static bool ShouldSkipDirectory(string relativePath) {
        if (string.IsNullOrWhiteSpace(relativePath)) {
            return false;
        }

        string firstSegment = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        return Array.Exists(
            RuntimeDirectoryNames,
            directoryName => string.Equals(directoryName, firstSegment, StringComparison.OrdinalIgnoreCase));
    }

    private void DeleteRunDirectory() {
        string fullRunDirectory = Path.GetFullPath(RunDirectory);
        string allowedRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Survivalcraft.Test"));
        if (!fullRunDirectory.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase)) {
            return;
        }

        try {
            if (Directory.Exists(fullRunDirectory)) {
                Directory.Delete(fullRunDirectory, true);
            }
        }
        catch {
            // Leaving a failed test directory behind is better than hiding the original test failure.
        }
    }

    private static int GetAvailableUdpPort() {
        using UdpClient udpClient = new(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)udpClient.Client.LocalEndPoint!).Port;
    }

    private static string SanitizePathSegment(string value) {
        foreach (char invalidChar in Path.GetInvalidFileNameChars()) {
            value = value.Replace(invalidChar, '_');
        }
        return value;
    }

}
