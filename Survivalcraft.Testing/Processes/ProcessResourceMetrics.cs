using System.Diagnostics;

namespace Survivalcraft.Testing.Processes;

public sealed record ProcessResourceSummary(
    int SampleCount,
    DateTimeOffset? FirstSampleAt,
    DateTimeOffset? LastSampleAt,
    long InitialWorkingSetBytes,
    long LastWorkingSetBytes,
    long PeakWorkingSetBytes,
    long InitialPrivateMemoryBytes,
    long LastPrivateMemoryBytes,
    long PeakPrivateMemoryBytes,
    int PeakHandleCount,
    int PeakThreadCount,
    long InitialProcessorTimeMilliseconds,
    long LastProcessorTimeMilliseconds) {

    public static ProcessResourceSummary Empty { get; } = new(
        0,
        null,
        null,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0);

    public long WorkingSetGrowthBytes =>
        LastWorkingSetBytes - InitialWorkingSetBytes;

    public long PrivateMemoryGrowthBytes =>
        LastPrivateMemoryBytes - InitialPrivateMemoryBytes;

    public long ProcessorTimeMilliseconds =>
        Math.Max(0, LastProcessorTimeMilliseconds - InitialProcessorTimeMilliseconds);

    public long SampledDurationMilliseconds =>
        FirstSampleAt.HasValue && LastSampleAt.HasValue
            ? Math.Max(
                0,
                (long)(LastSampleAt.Value - FirstSampleAt.Value).TotalMilliseconds)
            : 0;
}

internal sealed class ProcessResourceMonitor {
    private static readonly TimeSpan DefaultSampleInterval = TimeSpan.FromMilliseconds(250);

    private readonly object sync = new();
    private readonly Process process;
    private readonly TimeSpan sampleInterval;
    private int sampleCount;
    private DateTimeOffset? firstSampleAt;
    private DateTimeOffset? lastSampleAt;
    private long initialWorkingSetBytes;
    private long lastWorkingSetBytes;
    private long peakWorkingSetBytes;
    private long initialPrivateMemoryBytes;
    private long lastPrivateMemoryBytes;
    private long peakPrivateMemoryBytes;
    private int peakHandleCount;
    private int peakThreadCount;
    private long initialProcessorTimeMilliseconds;
    private long lastProcessorTimeMilliseconds;

    public ProcessResourceMonitor(
        Process process,
        TimeSpan? sampleInterval = null) {
        this.process = process ?? throw new ArgumentNullException(nameof(process));
        this.sampleInterval = sampleInterval ?? DefaultSampleInterval;
        if (this.sampleInterval <= TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(sampleInterval));
        }
    }

    public ProcessResourceSummary Summary {
        get {
            lock (sync) {
                return new ProcessResourceSummary(
                    sampleCount,
                    firstSampleAt,
                    lastSampleAt,
                    initialWorkingSetBytes,
                    lastWorkingSetBytes,
                    peakWorkingSetBytes,
                    initialPrivateMemoryBytes,
                    lastPrivateMemoryBytes,
                    peakPrivateMemoryBytes,
                    peakHandleCount,
                    peakThreadCount,
                    initialProcessorTimeMilliseconds,
                    lastProcessorTimeMilliseconds);
            }
        }
    }

    public bool Capture() {
        try {
            process.Refresh();
            if (process.HasExited) {
                return false;
            }

            DateTimeOffset sampledAt = DateTimeOffset.UtcNow;
            long workingSetBytes = process.WorkingSet64;
            long privateMemoryBytes = process.PrivateMemorySize64;
            int handleCount = process.HandleCount;
            int threadCount = process.Threads.Count;
            long processorTimeMilliseconds =
                process.TotalProcessorTime.Ticks / TimeSpan.TicksPerMillisecond;

            lock (sync) {
                if (sampleCount == 0) {
                    firstSampleAt = sampledAt;
                    initialWorkingSetBytes = workingSetBytes;
                    initialPrivateMemoryBytes = privateMemoryBytes;
                    initialProcessorTimeMilliseconds = processorTimeMilliseconds;
                }
                sampleCount++;
                lastSampleAt = sampledAt;
                lastWorkingSetBytes = workingSetBytes;
                lastPrivateMemoryBytes = privateMemoryBytes;
                lastProcessorTimeMilliseconds = processorTimeMilliseconds;
                peakWorkingSetBytes = Math.Max(peakWorkingSetBytes, workingSetBytes);
                peakPrivateMemoryBytes = Math.Max(peakPrivateMemoryBytes, privateMemoryBytes);
                peakHandleCount = Math.Max(peakHandleCount, handleCount);
                peakThreadCount = Math.Max(peakThreadCount, threadCount);
            }
            return true;
        }
        catch {
            // Resource telemetry must never hide the gameplay result. A zero-sample
            // summary remains explicit and is rejected by the soak budget gate.
            return false;
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken) {
        while (!cancellationToken.IsCancellationRequested) {
            if (!Capture()) {
                return;
            }
            try {
                await Task.Delay(sampleInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                return;
            }
        }
    }
}
