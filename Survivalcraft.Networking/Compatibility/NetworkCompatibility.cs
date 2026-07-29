using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Game.Network;

[Flags]
public enum NetworkCapabilities {
    None = 0,
    ApiModManifest = 1 << 0,
    StructuredCompatibilityResult = 1 << 1,
    ModPacketChannel = 1 << 2
}

public enum NetworkCompatibilityState : byte {
    NotStarted,
    AwaitingHello,
    AwaitingChannels,
    Accepted,
    Rejected
}

public enum NetworkCompatibilityIssueCode : byte {
    None,
    InvalidServerManifest,
    InvalidClientManifest,
    ProtocolMismatch,
    ApiVersionMismatch,
    MissingCapabilities,
    MissingClientMod,
    ExtraClientMod,
    ModVersionMismatch,
    ModApiVersionMismatch,
    HandshakeRequired,
    HandshakeTimeout,
    InvalidServerChannelCatalog,
    InvalidClientChannelCatalog,
    MissingClientChannel,
    ExtraClientChannel,
    ChannelVersionMismatch,
    ChannelDirectionMismatch,
    ChannelPayloadLimitMismatch
}

public sealed class NetworkModIdentity {
    public string PackageName = string.Empty;
    public string Version = string.Empty;
    public string ApiVersion = string.Empty;

    public NetworkModIdentity() {
    }

    public NetworkModIdentity(string packageName, string version, string apiVersion) {
        PackageName = packageName;
        Version = version;
        ApiVersion = apiVersion;
    }

    public NetworkModIdentity Clone() => new(PackageName, Version, ApiVersion);
}

public sealed class NetworkCompatibilityManifest {
    public int ProtocolRevision;
    public string ApiVersion = string.Empty;
    public NetworkCapabilities Capabilities;
    public List<NetworkModIdentity> Mods = [];

    public NetworkCompatibilityManifest() {
    }

    public NetworkCompatibilityManifest(
        int protocolRevision,
        string apiVersion,
        NetworkCapabilities capabilities,
        IEnumerable<NetworkModIdentity> mods) {
        ProtocolRevision = protocolRevision;
        ApiVersion = apiVersion;
        Capabilities = capabilities;
        Mods = mods?.Select(mod => mod?.Clone()).ToList() ?? [];
    }

    public NetworkCompatibilityManifest Clone() =>
        new(ProtocolRevision, ApiVersion, Capabilities, Mods);
}

public sealed class NetworkCompatibilityIssue {
    public NetworkCompatibilityIssueCode Code;
    public string PackageName = string.Empty;
    public string Expected = string.Empty;
    public string Actual = string.Empty;
    public string Message = string.Empty;

    public NetworkCompatibilityIssue() {
    }

    public NetworkCompatibilityIssue(
        NetworkCompatibilityIssueCode code,
        string packageName,
        string expected,
        string actual,
        string message) {
        Code = code;
        PackageName = packageName ?? string.Empty;
        Expected = expected ?? string.Empty;
        Actual = actual ?? string.Empty;
        Message = message ?? string.Empty;
    }
}

public sealed class NetworkCompatibilityResult {
    private const int MaximumSummaryLength = 8192;

    public bool Accepted;
    public string Summary = string.Empty;
    public List<NetworkCompatibilityIssue> Issues = [];

    public NetworkCompatibilityIssueCode PrimaryCode =>
        Issues.Count == 0 ? NetworkCompatibilityIssueCode.None : Issues[0].Code;

    public static NetworkCompatibilityResult Accept() => new() {
        Accepted = true,
        Summary = "Compatibility accepted."
    };

    public static NetworkCompatibilityResult Reject(IEnumerable<NetworkCompatibilityIssue> issues) {
        List<NetworkCompatibilityIssue> allIssues = issues?.ToList() ?? [];
        List<NetworkCompatibilityIssue> issueList = allIssues
            .Take(NetworkCompatibility.MaximumIssueCount)
            .ToList();
        string summary = issueList.Count == 0
            ? "Compatibility rejected."
            : string.Join("; ", issueList.Select(issue => issue.Message));
        if (allIssues.Count > issueList.Count) {
            summary += $"; {allIssues.Count - issueList.Count} additional issue(s) omitted.";
        }
        if (summary.Length > MaximumSummaryLength) {
            summary = summary[..(MaximumSummaryLength - 3)] + "...";
        }
        return new NetworkCompatibilityResult {
            Accepted = false,
            Issues = issueList,
            Summary = summary
        };
    }

    public string ToDisplayMessage() => string.IsNullOrWhiteSpace(Summary)
        ? "The server rejected this client's API/mod configuration."
        : Summary;
}

public static class NetworkCompatibility {
    public const int ProtocolRevision = 8;
    public const int MaximumModCount = 256;
    public const int MaximumIssueCount = 32;
    public const int MaximumFieldLength = 256;

    public const NetworkCapabilities CurrentCapabilities =
        NetworkCapabilities.ApiModManifest
        | NetworkCapabilities.StructuredCompatibilityResult
        | NetworkCapabilities.ModPacketChannel;

    public const NetworkCapabilities RequiredCapabilities =
        NetworkCapabilities.ApiModManifest
        | NetworkCapabilities.StructuredCompatibilityResult
        | NetworkCapabilities.ModPacketChannel;

    public static NetworkCompatibilityResult Evaluate(
        NetworkCompatibilityManifest serverManifest,
        NetworkCompatibilityManifest clientManifest) {
        List<NetworkCompatibilityIssue> issues = [];
        if (!TryCanonicalize(serverManifest, out NetworkCompatibilityManifest server, out string serverError)) {
            issues.Add(new NetworkCompatibilityIssue(
                NetworkCompatibilityIssueCode.InvalidServerManifest,
                string.Empty,
                "valid manifest",
                serverError,
                $"Server manifest is invalid: {serverError}"));
            return NetworkCompatibilityResult.Reject(issues);
        }
        if (!TryCanonicalize(clientManifest, out NetworkCompatibilityManifest client, out string clientError)) {
            issues.Add(new NetworkCompatibilityIssue(
                NetworkCompatibilityIssueCode.InvalidClientManifest,
                string.Empty,
                "valid manifest",
                clientError,
                $"Client manifest is invalid: {clientError}"));
            return NetworkCompatibilityResult.Reject(issues);
        }

        if (server.ProtocolRevision != client.ProtocolRevision) {
            issues.Add(new NetworkCompatibilityIssue(
                NetworkCompatibilityIssueCode.ProtocolMismatch,
                string.Empty,
                server.ProtocolRevision.ToString(),
                client.ProtocolRevision.ToString(),
                $"Multiplayer protocol mismatch (server {server.ProtocolRevision}, client {client.ProtocolRevision})."));
        }
        if (!string.Equals(server.ApiVersion, client.ApiVersion, StringComparison.Ordinal)) {
            issues.Add(new NetworkCompatibilityIssue(
                NetworkCompatibilityIssueCode.ApiVersionMismatch,
                string.Empty,
                server.ApiVersion,
                client.ApiVersion,
                $"API version mismatch (server {server.ApiVersion}, client {client.ApiVersion})."));
        }

        NetworkCapabilities required = RequiredCapabilities;
        NetworkCapabilities missing = required & ~client.Capabilities;
        if (missing != NetworkCapabilities.None) {
            issues.Add(new NetworkCompatibilityIssue(
                NetworkCompatibilityIssueCode.MissingCapabilities,
                string.Empty,
                required.ToString(),
                client.Capabilities.ToString(),
                $"Client is missing required multiplayer capabilities: {missing}."));
        }

        Dictionary<string, NetworkModIdentity> serverMods = server.Mods.ToDictionary(
            mod => mod.PackageName,
            StringComparer.OrdinalIgnoreCase);
        Dictionary<string, NetworkModIdentity> clientMods = client.Mods.ToDictionary(
            mod => mod.PackageName,
            StringComparer.OrdinalIgnoreCase);

        foreach (NetworkModIdentity expected in server.Mods) {
            if (!clientMods.TryGetValue(expected.PackageName, out NetworkModIdentity actual)) {
                issues.Add(new NetworkCompatibilityIssue(
                    NetworkCompatibilityIssueCode.MissingClientMod,
                    expected.PackageName,
                    FormatMod(expected),
                    "missing",
                    $"Client is missing required mod '{expected.PackageName}' ({expected.Version})."));
                continue;
            }
            if (!string.Equals(expected.Version, actual.Version, StringComparison.Ordinal)) {
                issues.Add(new NetworkCompatibilityIssue(
                    NetworkCompatibilityIssueCode.ModVersionMismatch,
                    expected.PackageName,
                    expected.Version,
                    actual.Version,
                    $"Mod '{expected.PackageName}' version mismatch (server {expected.Version}, client {actual.Version})."));
            }
            if (!string.Equals(expected.ApiVersion, actual.ApiVersion, StringComparison.Ordinal)) {
                issues.Add(new NetworkCompatibilityIssue(
                    NetworkCompatibilityIssueCode.ModApiVersionMismatch,
                    expected.PackageName,
                    expected.ApiVersion,
                    actual.ApiVersion,
                    $"Mod '{expected.PackageName}' API target mismatch (server {expected.ApiVersion}, client {actual.ApiVersion})."));
            }
        }
        foreach (NetworkModIdentity actual in client.Mods) {
            if (!serverMods.ContainsKey(actual.PackageName)) {
                issues.Add(new NetworkCompatibilityIssue(
                    NetworkCompatibilityIssueCode.ExtraClientMod,
                    actual.PackageName,
                    "absent",
                    FormatMod(actual),
                    $"Client has extra mod '{actual.PackageName}' ({actual.Version})."));
            }
        }

        return issues.Count == 0
            ? NetworkCompatibilityResult.Accept()
            : NetworkCompatibilityResult.Reject(issues);
    }

    public static NetworkCompatibilityResult CreateHandshakeRequiredResult(string packetName) =>
        NetworkCompatibilityResult.Reject([
            new NetworkCompatibilityIssue(
                NetworkCompatibilityIssueCode.HandshakeRequired,
                string.Empty,
                nameof(NetworkCompatibilityState.Accepted),
                packetName ?? string.Empty,
                $"Compatibility handshake must complete before '{packetName}' is accepted.")
        ]);

    public static NetworkCompatibilityResult CreateHandshakeTimeoutResult(
        int timeoutSeconds,
        NetworkCompatibilityState state = NetworkCompatibilityState.AwaitingHello) =>
        NetworkCompatibilityResult.Reject([
            new NetworkCompatibilityIssue(
                NetworkCompatibilityIssueCode.HandshakeTimeout,
                string.Empty,
                $"completed within {timeoutSeconds} seconds",
                state.ToString(),
                $"Compatibility handshake timed out after {timeoutSeconds} seconds.")
        ]);

    public static bool TryCanonicalize(
        NetworkCompatibilityManifest manifest,
        out NetworkCompatibilityManifest canonical,
        out string error) {
        canonical = null;
        error = string.Empty;
        if (manifest == null) {
            error = "manifest is null";
            return false;
        }

        string apiVersion = Normalize(manifest.ApiVersion);
        if (apiVersion.Length == 0) {
            error = "API version is empty";
            return false;
        }
        if (!IsFieldLengthValid(apiVersion)) {
            error = $"API version exceeds {MaximumFieldLength} characters";
            return false;
        }

        List<NetworkModIdentity> sourceMods = manifest.Mods ?? [];
        if (sourceMods.Count > MaximumModCount) {
            error = $"mod count {sourceMods.Count} exceeds {MaximumModCount}";
            return false;
        }

        List<NetworkModIdentity> mods = [];
        HashSet<string> packageNames = new(StringComparer.OrdinalIgnoreCase);
        foreach (NetworkModIdentity source in sourceMods) {
            if (source == null) {
                error = "manifest contains a null mod entry";
                return false;
            }
            string packageName = Normalize(source.PackageName).ToLowerInvariant();
            string version = Normalize(source.Version);
            string modApiVersion = Normalize(source.ApiVersion);
            if (packageName.Length == 0) {
                error = "manifest contains an empty package name";
                return false;
            }
            if (!IsFieldLengthValid(packageName)
                || !IsFieldLengthValid(version)
                || !IsFieldLengthValid(modApiVersion)) {
                error = $"mod '{packageName}' contains a field longer than {MaximumFieldLength} characters";
                return false;
            }
            if (!packageNames.Add(packageName)) {
                error = $"duplicate package '{packageName}'";
                return false;
            }
            mods.Add(new NetworkModIdentity(packageName, version, modApiVersion));
        }

        mods.Sort((left, right) => string.CompareOrdinal(left.PackageName, right.PackageName));
        canonical = new NetworkCompatibilityManifest(
            manifest.ProtocolRevision,
            apiVersion,
            manifest.Capabilities,
            mods);
        return true;
    }

    private static bool IsFieldLengthValid(string value) =>
        value.Length <= MaximumFieldLength;

    private static string Normalize(string value) =>
        value?.Trim() ?? string.Empty;

    private static string FormatMod(NetworkModIdentity mod) =>
        $"{mod.PackageName}@{mod.Version} (API {mod.ApiVersion})";
}

public static class NetworkCompatibilityWireCodec {
    public const int MaximumEncodedBytes = 1024 * 1024;
    private const int MaximumSummaryLength = 8192;
    private const int MaximumIssueValueLength = 2048;

    public static byte[] EncodeManifest(NetworkCompatibilityManifest manifest) {
        ArgumentNullException.ThrowIfNull(manifest);
        return Encode(writer => {
            writer.Write(manifest.ProtocolRevision);
            WriteString(writer, manifest.ApiVersion, NetworkCompatibility.MaximumFieldLength, "API version");
            writer.Write((int)manifest.Capabilities);
            int count = manifest.Mods?.Count ?? 0;
            ValidateCount(count, NetworkCompatibility.MaximumModCount, "mod");
            writer.Write(count);
            for (int index = 0; index < count; index++) {
                NetworkModIdentity mod = manifest.Mods[index]
                    ?? throw new InvalidDataException("Manifest contains a null mod entry.");
                WriteString(writer, mod.PackageName, NetworkCompatibility.MaximumFieldLength, "package name");
                WriteString(writer, mod.Version, NetworkCompatibility.MaximumFieldLength, "mod version");
                WriteString(writer, mod.ApiVersion, NetworkCompatibility.MaximumFieldLength, "mod API version");
            }
        });
    }

    public static NetworkCompatibilityManifest DecodeManifest(byte[] payload) =>
        Decode(payload, reader => {
            int protocolRevision = reader.ReadInt32();
            string apiVersion = ReadString(reader, NetworkCompatibility.MaximumFieldLength, "API version");
            NetworkCapabilities capabilities = (NetworkCapabilities)reader.ReadInt32();
            int count = reader.ReadInt32();
            ValidateCount(count, NetworkCompatibility.MaximumModCount, "mod");
            List<NetworkModIdentity> mods = new(count);
            for (int index = 0; index < count; index++) {
                mods.Add(new NetworkModIdentity(
                    ReadString(reader, NetworkCompatibility.MaximumFieldLength, "package name"),
                    ReadString(reader, NetworkCompatibility.MaximumFieldLength, "mod version"),
                    ReadString(reader, NetworkCompatibility.MaximumFieldLength, "mod API version")));
            }
            return new NetworkCompatibilityManifest(
                protocolRevision,
                apiVersion,
                capabilities,
                mods);
        });

    public static byte[] EncodeResult(NetworkCompatibilityResult result) {
        ArgumentNullException.ThrowIfNull(result);
        return Encode(writer => {
            writer.Write(result.Accepted);
            WriteString(writer, result.Summary, MaximumSummaryLength, "summary");
            int count = result.Issues?.Count ?? 0;
            ValidateCount(count, NetworkCompatibility.MaximumIssueCount, "issue");
            writer.Write(count);
            for (int index = 0; index < count; index++) {
                NetworkCompatibilityIssue issue = result.Issues[index]
                    ?? throw new InvalidDataException("Result contains a null issue entry.");
                writer.Write((byte)issue.Code);
                WriteString(writer, issue.PackageName, NetworkCompatibility.MaximumFieldLength, "issue package name");
                WriteString(writer, issue.Expected, MaximumIssueValueLength, "expected value");
                WriteString(writer, issue.Actual, MaximumIssueValueLength, "actual value");
                WriteString(writer, issue.Message, MaximumIssueValueLength, "issue message");
            }
        });
    }

    public static NetworkCompatibilityResult DecodeResult(byte[] payload) =>
        Decode(payload, reader => {
            bool accepted = reader.ReadBoolean();
            string summary = ReadString(reader, MaximumSummaryLength, "summary");
            int count = reader.ReadInt32();
            ValidateCount(count, NetworkCompatibility.MaximumIssueCount, "issue");
            List<NetworkCompatibilityIssue> issues = new(count);
            for (int index = 0; index < count; index++) {
                issues.Add(new NetworkCompatibilityIssue(
                    (NetworkCompatibilityIssueCode)reader.ReadByte(),
                    ReadString(reader, NetworkCompatibility.MaximumFieldLength, "issue package name"),
                    ReadString(reader, MaximumIssueValueLength, "expected value"),
                    ReadString(reader, MaximumIssueValueLength, "actual value"),
                    ReadString(reader, MaximumIssueValueLength, "issue message")));
            }
            return new NetworkCompatibilityResult {
                Accepted = accepted,
                Summary = summary,
                Issues = issues
            };
        });

    private static byte[] Encode(Action<BinaryWriter> write) {
        using MemoryStream stream = new();
        using (BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true)) {
            write(writer);
            writer.Flush();
        }
        if (stream.Length > MaximumEncodedBytes) {
            throw new InvalidDataException(
                $"Compatibility payload exceeds {MaximumEncodedBytes} bytes.");
        }
        return stream.ToArray();
    }

    private static T Decode<T>(byte[] payload, Func<BinaryReader, T> read) {
        if (payload == null || payload.Length == 0 || payload.Length > MaximumEncodedBytes) {
            throw new InvalidDataException(
                $"Compatibility payload length must be between 1 and {MaximumEncodedBytes} bytes.");
        }
        try {
            using MemoryStream stream = new(payload, writable: false);
            using BinaryReader reader = new(stream, Encoding.UTF8, leaveOpen: true);
            T value = read(reader);
            if (stream.Position != stream.Length) {
                throw new InvalidDataException("Compatibility payload contains trailing data.");
            }
            return value;
        }
        catch (EndOfStreamException exception) {
            throw new InvalidDataException("Compatibility payload is truncated.", exception);
        }
    }

    private static void WriteString(
        BinaryWriter writer,
        string value,
        int maximumLength,
        string fieldName) {
        value ??= string.Empty;
        ValidateString(value, maximumLength, fieldName);
        writer.Write(value);
    }

    private static string ReadString(
        BinaryReader reader,
        int maximumLength,
        string fieldName) {
        string value = reader.ReadString();
        ValidateString(value, maximumLength, fieldName);
        return value;
    }

    private static void ValidateString(string value, int maximumLength, string fieldName) {
        if (value.Length > maximumLength) {
            throw new InvalidDataException(
                $"Compatibility {fieldName} exceeds {maximumLength} characters.");
        }
    }

    private static void ValidateCount(int count, int maximum, string kind) {
        if (count < 0 || count > maximum) {
            throw new InvalidDataException(
                $"Compatibility {kind} count {count} is outside 0..{maximum}.");
        }
    }
}
