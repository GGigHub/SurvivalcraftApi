using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Survivalcraft.Testing.Processes;

/// <summary>
/// Allocates UDP ports from a non-ephemeral range and records short-lived leases
/// in a machine-local file. The file closes the race between independently
/// launched TestHost processes; the in-process set also keeps ports unique for
/// nodes that have been constructed but have not bound their game socket yet.
/// </summary>
internal static class UdpPortLeaseAllocator {
    internal const int MinimumPort = 20_000;
    internal const int MaximumPort = 45_000;
    internal static readonly TimeSpan LeaseCooldown = TimeSpan.FromMinutes(15);

    private const int FileLockAttempts = 50;
    private const int FileLockRetryMilliseconds = 20;
    private static readonly object Sync = new();
    private static readonly HashSet<int> ProcessLeases = [];
    private static readonly string LeaseFilePath = Path.Combine(
        Path.GetTempPath(),
        "Survivalcraft.Testing",
        "udp-port-leases-v1.txt");
    private static int nextCandidate = Random.Shared.Next(MinimumPort, MaximumPort + 1);

    public static int Allocate() {
        lock (Sync) {
            string? directory = Path.GetDirectoryName(LeaseFilePath);
            if (directory != null) {
                Directory.CreateDirectory(directory);
            }

            IOException? lastLockFailure = null;
            for (int attempt = 0; attempt < FileLockAttempts; attempt++) {
                try {
                    using FileStream leaseFile = new(
                        LeaseFilePath,
                        FileMode.OpenOrCreate,
                        FileAccess.ReadWrite,
                        FileShare.None);
                    return AllocateWithExclusiveFileLock(leaseFile);
                }
                catch (IOException exception) {
                    lastLockFailure = exception;
                    Thread.Sleep(FileLockRetryMilliseconds);
                }
            }

            throw new InvalidOperationException(
                $"Could not acquire the shared UDP port lease file '{LeaseFilePath}'.",
                lastLockFailure);
        }
    }

    private static int AllocateWithExclusiveFileLock(FileStream leaseFile) {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long oldestValidLease = now - (long)LeaseCooldown.TotalMilliseconds;
        Dictionary<int, long> leases = ReadLeases(leaseFile)
            .Where(pair => pair.Value >= oldestValidLease)
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        // Refresh all leases owned by this process. A long-running test process
        // must not age out a node that was constructed earlier but starts later.
        foreach (int port in ProcessLeases) {
            leases[port] = now;
        }

        int rangeLength = MaximumPort - MinimumPort + 1;
        for (int attempt = 0; attempt < rangeLength; attempt++) {
            int candidate = nextCandidate;
            nextCandidate = candidate == MaximumPort ? MinimumPort : candidate + 1;
            if (leases.ContainsKey(candidate) || !CanBind(candidate)) {
                continue;
            }

            leases[candidate] = now;
            ProcessLeases.Add(candidate);
            WriteLeases(leaseFile, leases);
            return candidate;
        }

        throw new InvalidOperationException(
            $"Could not reserve a UDP port in the AutoTest range {MinimumPort}-{MaximumPort}.");
    }

    private static Dictionary<int, long> ReadLeases(FileStream leaseFile) {
        Dictionary<int, long> leases = [];
        leaseFile.Position = 0;
        using StreamReader reader = new(
            leaseFile,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 4096,
            leaveOpen: true);
        while (reader.ReadLine() is { } line) {
            int separator = line.IndexOf('|');
            if (separator <= 0 || separator == line.Length - 1) {
                continue;
            }
            if (int.TryParse(
                    line.AsSpan(0, separator),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int port)
                && long.TryParse(
                    line.AsSpan(separator + 1),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out long timestamp)
                && port is >= MinimumPort and <= MaximumPort) {
                leases[port] = timestamp;
            }
        }
        return leases;
    }

    private static void WriteLeases(FileStream leaseFile, IReadOnlyDictionary<int, long> leases) {
        leaseFile.Position = 0;
        leaseFile.SetLength(0);
        using StreamWriter writer = new(
            leaseFile,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 4096,
            leaveOpen: true);
        foreach ((int port, long timestamp) in leases.OrderBy(pair => pair.Key)) {
            writer.Write(port.ToString(CultureInfo.InvariantCulture));
            writer.Write('|');
            writer.WriteLine(timestamp.ToString(CultureInfo.InvariantCulture));
        }
        writer.Flush();
        leaseFile.Flush(flushToDisk: true);
    }

    private static bool CanBind(int port) {
        try {
            using Socket socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) {
                ExclusiveAddressUse = true
            };
            socket.Bind(new IPEndPoint(IPAddress.Any, port));
            return true;
        }
        catch (SocketException) {
            return false;
        }
    }
}
