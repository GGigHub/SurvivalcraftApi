using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;

namespace Survivalcraft.Testing.Processes;

public sealed record RelayServerOptions(
    string RuntimeDirectory,
    int Port = 0,
    TimeSpan? StartupTimeout = null,
    string AssemblyName = "Survivalcraft.RelayServer.dll");

public sealed class RelayServerProcess : IAsyncDisposable {
    private const int MaximumRetainedLogLines = 10_000;

    private readonly RelayServerOptions options;
    private readonly ConcurrentQueue<string> logs = new();
    private readonly TaskCompletionSource<bool> ready = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private Process? process;
    private bool disposed;

    public RelayServerProcess(RelayServerOptions options) {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        ArgumentException.ThrowIfNullOrWhiteSpace(options.RuntimeDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.AssemblyName);
        if (options.Port is < 0 or > IPEndPoint.MaxPort) {
            throw new ArgumentOutOfRangeException(nameof(options.Port));
        }
        RuntimeDirectory = Path.GetFullPath(options.RuntimeDirectory);
        Port = options.Port == 0 ? UdpPortLeaseAllocator.Allocate() : options.Port;
    }

    public string RuntimeDirectory { get; }
    public int Port { get; }
    public int? ProcessId => process?.Id;
    public bool IsRunning => process is { HasExited: false };
    public IReadOnlyList<string> Logs => logs.ToArray();

    public async Task StartAsync(CancellationToken cancellationToken = default) {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (process != null) {
            throw new InvalidOperationException("The relay server process has already been started.");
        }

        string assemblyPath = Path.Combine(RuntimeDirectory, options.AssemblyName);
        if (!File.Exists(assemblyPath)) {
            throw new FileNotFoundException(
                "The relay server assembly was not found in the runtime directory.",
                assemblyPath);
        }

        ProcessStartInfo startInfo = new("dotnet") {
            WorkingDirectory = RuntimeDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(assemblyPath);
        startInfo.ArgumentList.Add(Port.ToString(CultureInfo.InvariantCulture));
        startInfo.Environment["DOTNET_NOLOGO"] = "1";

        Process relayProcess = new() {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };
        relayProcess.OutputDataReceived += (_, eventArgs) =>
            AppendLog("stdout", eventArgs.Data);
        relayProcess.ErrorDataReceived += (_, eventArgs) =>
            AppendLog("stderr", eventArgs.Data);
        relayProcess.Exited += (_, _) => {
            if (!ready.Task.IsCompleted) {
                ready.TrySetException(new InvalidOperationException(
                    $"Relay server exited before reporting readiness. ExitCode={GetExitCodeText(relayProcess)}."));
            }
        };

        if (!relayProcess.Start()) {
            relayProcess.Dispose();
            throw new InvalidOperationException("Failed to start the relay server process.");
        }
        process = relayProcess;
        relayProcess.BeginOutputReadLine();
        relayProcess.BeginErrorReadLine();

        try {
            await ready.Task.WaitAsync(
                options.StartupTimeout ?? TimeSpan.FromSeconds(15),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) {
            TryKillProcess();
            throw new InvalidOperationException(
                $"Relay server did not become ready on UDP {Port}.{Environment.NewLine}{GetRecentLogText()}",
                exception);
        }
    }

    public void ThrowIfUnhealthy() {
        if (process is { HasExited: true }) {
            throw new InvalidOperationException(
                $"Relay server exited unexpectedly. ExitCode={GetExitCodeText(process)}."
                + Environment.NewLine
                + GetRecentLogText());
        }
    }

    public string GetRecentLogText(int maximumLines = 120) {
        string[] current = logs.ToArray();
        int start = Math.Max(0, current.Length - maximumLines);
        return string.Join(Environment.NewLine, current[start..]);
    }

    private void AppendLog(string stream, string? line) {
        if (line == null) {
            return;
        }
        logs.Enqueue($"[{stream}] {line}");
        while (logs.Count > MaximumRetainedLogLines) {
            logs.TryDequeue(out _);
        }
        if (line.Contains("relay listening on UDP", StringComparison.OrdinalIgnoreCase)) {
            ready.TrySetResult(true);
        }
    }

    private static string GetExitCodeText(Process target) {
        try {
            return target.HasExited
                ? target.ExitCode.ToString(CultureInfo.InvariantCulture)
                : "running";
        }
        catch {
            return "unknown";
        }
    }

    private void TryKillProcess() {
        try {
            if (process is { HasExited: false }) {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch {
            // Best-effort cleanup must not hide the original scenario result.
        }
    }

    public ValueTask DisposeAsync() {
        if (disposed) {
            return ValueTask.CompletedTask;
        }
        disposed = true;
        TryKillProcess();
        process?.Dispose();
        return ValueTask.CompletedTask;
    }
}
