using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Survivalcraft.Testing.Control;

namespace Survivalcraft.Testing.Processes;

public sealed record GameNodeOptions(
    string Role,
    string RuntimeDirectory,
    string DataDirectory,
    string ExecutableName = "Survivalcraft.exe",
    TimeSpan? StartupTimeout = null,
    IReadOnlyDictionary<string, string>? AdditionalArguments = null);

public sealed class GameNodeProcess : IAsyncDisposable {
    private const int MaximumRetainedLogLines = 10_000;

    private readonly GameNodeOptions options;
    private readonly CancellationTokenSource disposeSource = new();
    private readonly ConcurrentQueue<string> logs = new();
    private readonly UdpClient logServer;
    private readonly Task logReceiveTask;
    private readonly NamedPipeControlClient controlClient;
    private Process? process;
    private string? fatalLogLine;
    private bool disposed;

    public GameNodeProcess(GameNodeOptions options) {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Role);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.RuntimeDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.DataDirectory);

        RuntimeDirectory = Path.GetFullPath(options.RuntimeDirectory);
        DataDirectory = Path.GetFullPath(options.DataDirectory);
        ControlPipeName = $"sc-test-{Sanitize(options.Role)}-{Guid.NewGuid():N}";
        InstanceId = $"{Sanitize(options.Role)}-{Guid.NewGuid():N}";
        GamePort = UdpPortLeaseAllocator.Allocate();
        CommandPort = UdpPortLeaseAllocator.Allocate();
        LogPort = UdpPortLeaseAllocator.Allocate();
        logServer = new UdpClient(AddressFamily.InterNetwork);
        logServer.Client.ExclusiveAddressUse = true;
        logServer.Client.Bind(new IPEndPoint(IPAddress.Loopback, LogPort));
        controlClient = new NamedPipeControlClient(ControlPipeName);
        logReceiveTask = Task.Run(ReceiveLogsAsync);
    }

    public string Role => options.Role;
    public string RuntimeDirectory { get; }
    public string DataDirectory { get; }
    public string ControlPipeName { get; }
    public string InstanceId { get; }
    public int GamePort { get; }
    public int CommandPort { get; }
    public int LogPort { get; }
    public int? ProcessId => process?.Id;
    public bool IsRunning => process is { HasExited: false };
    public IReadOnlyList<string> Logs => logs.ToArray();
    public string? FatalLogLine => Volatile.Read(ref fatalLogLine);

    public async Task StartAsync(CancellationToken cancellationToken = default) {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (process != null) {
            throw new InvalidOperationException($"Node '{Role}' has already been started.");
        }

        string executablePath = Path.Combine(RuntimeDirectory, options.ExecutableName);
        if (!File.Exists(executablePath)) {
            throw new FileNotFoundException(
                $"Game executable for node '{Role}' was not found.",
                executablePath);
        }

        Directory.CreateDirectory(DataDirectory);
        ProcessStartInfo startInfo = new() {
            FileName = executablePath,
            WorkingDirectory = RuntimeDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            CreateNoWindow = true
        };
        startInfo.Environment["SC_TEST_DISABLE_RESTART"] = "1";
        AddArgument(startInfo, "--log-port", LogPort.ToString());
        AddArgument(startInfo, "--cmd-port", CommandPort.ToString());
        AddArgument(startInfo, "--game-port", GamePort.ToString());
        AddArgument(startInfo, "--control-pipe", ControlPipeName);
        AddArgument(startInfo, "--instance-id", InstanceId);
        AddArgument(startInfo, "--data-dir", DataDirectory);
        AddArgument(startInfo, "--role", Role);
        startInfo.ArgumentList.Add("--headless");
        if (options.AdditionalArguments != null) {
            foreach ((string key, string value) in options.AdditionalArguments) {
                AddArgument(startInfo, "--" + key.TrimStart('-'), value);
            }
        }

        process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start game node '{Role}'.");
        try {
            await controlClient.ConnectAsync(
                options.StartupTimeout ?? TimeSpan.FromSeconds(60),
                cancellationToken).ConfigureAwait(false);
            _ = await SendAsync<PingCommand, PingResult>(
                ControlCommandNames.Ping,
                new PingCommand("startup"),
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) {
            TryKillProcess();
            throw new InvalidOperationException(
                $"Node '{Role}' did not expose its control channel.{Environment.NewLine}{GetRecentLogText()}",
                exception);
        }
    }

    public async Task<TResult> SendAsync<TPayload, TResult>(
        string command,
        TPayload payload,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) {
        ThrowIfUnhealthy();
        if (!IsRunning) {
            throw new InvalidOperationException(
                $"Node '{Role}' is not running. ExitCode={GetExitCodeText()}.{Environment.NewLine}{GetRecentLogText()}");
        }
        TResult result = await controlClient.SendAsync<TPayload, TResult>(
            command,
            payload,
            timeout,
            cancellationToken).ConfigureAwait(false);
        ThrowIfUnhealthy();
        return result;
    }

    public Task<NodeStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
        SendAsync<EmptyCommand, NodeStatus>(
            ControlCommandNames.GetStatus,
            new EmptyCommand(),
            cancellationToken: cancellationToken);

    public Task<AutomationDescription> DescribeAutomationAsync(
        CancellationToken cancellationToken = default) =>
        SendAsync<EmptyCommand, AutomationDescription>(
            ControlCommandNames.Describe,
            new EmptyCommand(),
            cancellationToken: cancellationToken);

    public async Task<AutomationDescription> RequireCapabilitiesAsync(
        RequiredAutomationCapabilities required,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(required);
        AutomationDescription description = await DescribeAutomationAsync(cancellationToken)
            .ConfigureAwait(false);
        AutomationCapabilityValidator.EnsureSupported(description, required);
        return description;
    }

    public async Task<NodeStatus> WaitForStatusAsync(
        Func<NodeStatus, bool> predicate,
        TimeSpan timeout,
        string failureMessage,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(predicate);
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        NodeStatus? last = null;
        Exception? lastError = null;

        while (DateTimeOffset.UtcNow < deadline) {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsRunning) {
                throw new InvalidOperationException(
                    $"Node '{Role}' exited while waiting for status. ExitCode={GetExitCodeText()}.{Environment.NewLine}{GetRecentLogText()}");
            }
            try {
                last = await GetStatusAsync(cancellationToken).ConfigureAwait(false);
                lastError = null;
                if (predicate(last)) {
                    return last;
                }
            }
            catch (Exception exception) when (exception is IOException or TimeoutException) {
                lastError = exception;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"{failureMessage} Node={Role}; Last={last}; LastError={lastError?.Message}.{Environment.NewLine}{GetRecentLogText()}");
    }

    public string GetRecentLogText(int maximumLines = 120) {
        string[] current = logs.ToArray();
        int start = Math.Max(0, current.Length - maximumLines);
        return string.Join(Environment.NewLine, current[start..]);
    }

    public void ThrowIfUnhealthy() {
        string? fatal = FatalLogLine;
        if (fatal != null) {
            throw new InvalidOperationException(
                $"Node '{Role}' emitted a fatal error log: {fatal}{Environment.NewLine}{GetRecentLogText()}");
        }
    }

    private async Task ReceiveLogsAsync() {
        while (!disposeSource.IsCancellationRequested) {
            try {
                UdpReceiveResult result = await logServer.ReceiveAsync(
                    disposeSource.Token).ConfigureAwait(false);
                string message = Encoding.UTF8.GetString(result.Buffer).TrimEnd();
                logs.Enqueue(message);
                if (IsFatalLog(message)) {
                    Interlocked.CompareExchange(ref fatalLogLine, message, null);
                }
                while (logs.Count > MaximumRetainedLogLines) {
                    logs.TryDequeue(out _);
                }
            }
            catch (OperationCanceledException) when (disposeSource.IsCancellationRequested) {
                break;
            }
            catch (ObjectDisposedException) {
                break;
            }
            catch (SocketException) when (disposeSource.IsCancellationRequested) {
                break;
            }
        }
    }

    private static void AddArgument(ProcessStartInfo startInfo, string key, string value) {
        startInfo.ArgumentList.Add(key);
        startInfo.ArgumentList.Add(value);
    }

    private static bool IsFatalLog(string message) =>
        message.Contains("[Error]", StringComparison.OrdinalIgnoreCase)
        || message.Contains("Unhandled exception", StringComparison.OrdinalIgnoreCase);

    private static string Sanitize(string value) {
        StringBuilder builder = new(value.Length);
        foreach (char character in value) {
            builder.Append(char.IsAsciiLetterOrDigit(character) ? character : '-');
        }
        return builder.ToString().Trim('-');
    }

    private string GetExitCodeText() {
        try {
            return process is { HasExited: true } ? process.ExitCode.ToString() : "running";
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
            // Best-effort cleanup. The original failure remains more useful.
        }
    }

    public async ValueTask DisposeAsync() {
        if (disposed) {
            return;
        }
        disposed = true;
        await controlClient.DisposeAsync().ConfigureAwait(false);
        TryKillProcess();
        process?.Dispose();
        disposeSource.Cancel();
        logServer.Dispose();
        try {
            await logReceiveTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) {
            // Expected during shutdown.
        }
        disposeSource.Dispose();
    }
}
