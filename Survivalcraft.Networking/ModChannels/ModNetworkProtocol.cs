using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Game.Network;

[Flags]
public enum ModNetworkDirection : byte {
    None = 0,
    ClientToServer = 1 << 0,
    ServerToClient = 1 << 1,
    Bidirectional = ClientToServer | ServerToClient
}

public enum ModNetworkDispatchCode : byte {
    Delivered,
    Duplicate,
    UnknownChannel,
    VersionMismatch,
    DirectionDenied,
    PayloadTooLarge,
    RateLimited,
    ProjectUnavailable,
    HandlerFailed
}

public sealed class ModNetworkChannelDescriptor {
    public string PackageName = string.Empty;
    public string ChannelName = string.Empty;
    public int Version;
    public ModNetworkDirection Directions;
    public int MaximumPayloadBytes;

    public ModNetworkChannelDescriptor() {
    }

    public ModNetworkChannelDescriptor(
        string packageName,
        string channelName,
        int version,
        ModNetworkDirection directions,
        int maximumPayloadBytes) {
        PackageName = packageName;
        ChannelName = channelName;
        Version = version;
        Directions = directions;
        MaximumPayloadBytes = maximumPayloadBytes;
    }

    public string QualifiedName => $"{PackageName}:{ChannelName}";

    public ModNetworkChannelDescriptor Clone() =>
        new(PackageName, ChannelName, Version, Directions, MaximumPayloadBytes);
}

public sealed class ModNetworkChannelCatalog {
    public List<ModNetworkChannelDescriptor> Channels = [];

    public ModNetworkChannelCatalog() {
    }

    public ModNetworkChannelCatalog(IEnumerable<ModNetworkChannelDescriptor> channels) {
        Channels = channels?.Select(channel => channel?.Clone()).ToList() ?? [];
    }

    public ModNetworkChannelCatalog Clone() => new(Channels);
}

public sealed class ModNetworkEnvelope {
    public Guid MessageId;
    public string PackageName = string.Empty;
    public string ChannelName = string.Empty;
    public int ChannelVersion;
    public byte[] Payload = [];

    public ModNetworkEnvelope() {
    }

    public ModNetworkEnvelope(
        Guid messageId,
        string packageName,
        string channelName,
        int channelVersion,
        byte[] payload) {
        MessageId = messageId;
        PackageName = packageName;
        ChannelName = channelName;
        ChannelVersion = channelVersion;
        Payload = payload?.ToArray() ?? [];
    }
}

public static class ModNetworkProtocol {
    public const byte CatalogWireVersion = 1;
    public const byte EnvelopeWireVersion = 1;
    public const int MaximumChannelCount = 512;
    public const int MaximumChannelsPerPackage = 32;
    public const int MaximumPackageNameLength = 256;
    public const int MaximumChannelNameLength = 96;
    public const int MaximumPayloadBytes = 64 * 1024;
    public const int DefaultMaximumPayloadBytes = 16 * 1024;
    public const int MaximumCatalogEncodedBytes = 256 * 1024;
    public const int MaximumEnvelopeEncodedBytes = 128 * 1024;

    public static NetworkCompatibilityResult EvaluateCatalogs(
        ModNetworkChannelCatalog serverCatalog,
        ModNetworkChannelCatalog clientCatalog) {
        List<NetworkCompatibilityIssue> issues = [];
        if (!TryCanonicalizeCatalog(
                serverCatalog,
                out ModNetworkChannelCatalog server,
                out string serverError)) {
            return NetworkCompatibilityResult.Reject([
                new NetworkCompatibilityIssue(
                    NetworkCompatibilityIssueCode.InvalidServerChannelCatalog,
                    string.Empty,
                    "valid channel catalog",
                    serverError,
                    $"Server mod channel catalog is invalid: {serverError}")
            ]);
        }
        if (!TryCanonicalizeCatalog(
                clientCatalog,
                out ModNetworkChannelCatalog client,
                out string clientError)) {
            return NetworkCompatibilityResult.Reject([
                new NetworkCompatibilityIssue(
                    NetworkCompatibilityIssueCode.InvalidClientChannelCatalog,
                    string.Empty,
                    "valid channel catalog",
                    clientError,
                    $"Client mod channel catalog is invalid: {clientError}")
            ]);
        }

        Dictionary<string, ModNetworkChannelDescriptor> serverChannels = server.Channels.ToDictionary(
            BuildKey,
            StringComparer.Ordinal);
        Dictionary<string, ModNetworkChannelDescriptor> clientChannels = client.Channels.ToDictionary(
            BuildKey,
            StringComparer.Ordinal);

        foreach (ModNetworkChannelDescriptor expected in server.Channels) {
            if (!clientChannels.TryGetValue(BuildKey(expected), out ModNetworkChannelDescriptor actual)) {
                issues.Add(new NetworkCompatibilityIssue(
                    NetworkCompatibilityIssueCode.MissingClientChannel,
                    expected.QualifiedName,
                    FormatDescriptor(expected),
                    "missing",
                    $"Client is missing mod channel '{expected.QualifiedName}'."));
                continue;
            }
            if (expected.Version != actual.Version) {
                issues.Add(new NetworkCompatibilityIssue(
                    NetworkCompatibilityIssueCode.ChannelVersionMismatch,
                    expected.QualifiedName,
                    expected.Version.ToString(),
                    actual.Version.ToString(),
                    $"Mod channel '{expected.QualifiedName}' version mismatch (server {expected.Version}, client {actual.Version})."));
            }
            if (expected.Directions != actual.Directions) {
                issues.Add(new NetworkCompatibilityIssue(
                    NetworkCompatibilityIssueCode.ChannelDirectionMismatch,
                    expected.QualifiedName,
                    expected.Directions.ToString(),
                    actual.Directions.ToString(),
                    $"Mod channel '{expected.QualifiedName}' direction mismatch (server {expected.Directions}, client {actual.Directions})."));
            }
            if (expected.MaximumPayloadBytes != actual.MaximumPayloadBytes) {
                issues.Add(new NetworkCompatibilityIssue(
                    NetworkCompatibilityIssueCode.ChannelPayloadLimitMismatch,
                    expected.QualifiedName,
                    expected.MaximumPayloadBytes.ToString(),
                    actual.MaximumPayloadBytes.ToString(),
                    $"Mod channel '{expected.QualifiedName}' payload limit mismatch (server {expected.MaximumPayloadBytes}, client {actual.MaximumPayloadBytes})."));
            }
        }
        foreach (ModNetworkChannelDescriptor actual in client.Channels) {
            if (!serverChannels.ContainsKey(BuildKey(actual))) {
                issues.Add(new NetworkCompatibilityIssue(
                    NetworkCompatibilityIssueCode.ExtraClientChannel,
                    actual.QualifiedName,
                    "absent",
                    FormatDescriptor(actual),
                    $"Client has extra mod channel '{actual.QualifiedName}'."));
            }
        }

        return issues.Count == 0
            ? NetworkCompatibilityResult.Accept()
            : NetworkCompatibilityResult.Reject(issues);
    }

    public static bool TryCanonicalizeCatalog(
        ModNetworkChannelCatalog catalog,
        out ModNetworkChannelCatalog canonical,
        out string error) {
        canonical = null;
        error = string.Empty;
        if (catalog == null) {
            error = "catalog is null";
            return false;
        }

        List<ModNetworkChannelDescriptor> source = catalog.Channels ?? [];
        if (source.Count > MaximumChannelCount) {
            error = $"channel count {source.Count} exceeds {MaximumChannelCount}";
            return false;
        }

        List<ModNetworkChannelDescriptor> channels = [];
        HashSet<string> keys = new(StringComparer.Ordinal);
        Dictionary<string, int> packageCounts = new(StringComparer.Ordinal);
        foreach (ModNetworkChannelDescriptor descriptor in source) {
            if (!TryCanonicalizeDescriptor(descriptor, out ModNetworkChannelDescriptor item, out error)) {
                return false;
            }
            string key = BuildKey(item);
            if (!keys.Add(key)) {
                error = $"duplicate channel '{item.QualifiedName}'";
                return false;
            }
            int packageCount = packageCounts.GetValueOrDefault(item.PackageName) + 1;
            if (packageCount > MaximumChannelsPerPackage) {
                error = $"package '{item.PackageName}' exceeds {MaximumChannelsPerPackage} channels";
                return false;
            }
            packageCounts[item.PackageName] = packageCount;
            channels.Add(item);
        }

        channels.Sort((left, right) => {
            int packageComparison = string.CompareOrdinal(left.PackageName, right.PackageName);
            return packageComparison != 0
                ? packageComparison
                : string.CompareOrdinal(left.ChannelName, right.ChannelName);
        });
        long encodedBytes = 1 + sizeof(int);
        foreach (ModNetworkChannelDescriptor channel in channels) {
            encodedBytes += GetEncodedStringLength(channel.PackageName);
            encodedBytes += GetEncodedStringLength(channel.ChannelName);
            encodedBytes += sizeof(int) + sizeof(byte) + sizeof(int);
        }
        if (encodedBytes > MaximumCatalogEncodedBytes) {
            error = $"encoded catalog size {encodedBytes} exceeds {MaximumCatalogEncodedBytes} bytes";
            return false;
        }
        canonical = new ModNetworkChannelCatalog(channels);
        return true;
    }

    public static bool TryCanonicalizeDescriptor(
        ModNetworkChannelDescriptor descriptor,
        out ModNetworkChannelDescriptor canonical,
        out string error) {
        canonical = null;
        error = string.Empty;
        if (descriptor == null) {
            error = "catalog contains a null channel";
            return false;
        }

        string packageName = Normalize(descriptor.PackageName);
        string channelName = Normalize(descriptor.ChannelName);
        if (!IsValidIdentifier(packageName, MaximumPackageNameLength)) {
            error = $"invalid package name '{packageName}'";
            return false;
        }
        if (!IsValidIdentifier(channelName, MaximumChannelNameLength)) {
            error = $"invalid channel name '{channelName}'";
            return false;
        }
        if (descriptor.Version <= 0) {
            error = $"channel '{packageName}:{channelName}' version must be positive";
            return false;
        }
        if (descriptor.Directions == ModNetworkDirection.None
            || (descriptor.Directions & ~ModNetworkDirection.Bidirectional) != 0) {
            error = $"channel '{packageName}:{channelName}' has invalid directions {descriptor.Directions}";
            return false;
        }
        if (descriptor.MaximumPayloadBytes <= 0
            || descriptor.MaximumPayloadBytes > MaximumPayloadBytes) {
            error = $"channel '{packageName}:{channelName}' payload limit must be between 1 and {MaximumPayloadBytes}";
            return false;
        }

        canonical = new ModNetworkChannelDescriptor(
            packageName,
            channelName,
            descriptor.Version,
            descriptor.Directions,
            descriptor.MaximumPayloadBytes);
        return true;
    }

    public static byte[] EncodeCatalog(ModNetworkChannelCatalog catalog) {
        if (!TryCanonicalizeCatalog(catalog, out ModNetworkChannelCatalog canonical, out string error)) {
            throw new InvalidDataException($"Invalid mod channel catalog: {error}");
        }
        return Encode(
            MaximumCatalogEncodedBytes,
            writer => {
                writer.Write(CatalogWireVersion);
                writer.Write(canonical.Channels.Count);
                foreach (ModNetworkChannelDescriptor descriptor in canonical.Channels) {
                    writer.Write(descriptor.PackageName);
                    writer.Write(descriptor.ChannelName);
                    writer.Write(descriptor.Version);
                    writer.Write((byte)descriptor.Directions);
                    writer.Write(descriptor.MaximumPayloadBytes);
                }
            });
    }

    public static ModNetworkChannelCatalog DecodeCatalog(byte[] payload) {
        ModNetworkChannelCatalog catalog = Decode(
            payload,
            MaximumCatalogEncodedBytes,
            reader => {
                byte wireVersion = reader.ReadByte();
                if (wireVersion != CatalogWireVersion) {
                    throw new InvalidDataException(
                        $"Unsupported mod channel catalog wire version {wireVersion}.");
                }
                int count = reader.ReadInt32();
                if (count < 0 || count > MaximumChannelCount) {
                    throw new InvalidDataException(
                        $"Mod channel count {count} is outside 0..{MaximumChannelCount}.");
                }
                List<ModNetworkChannelDescriptor> channels = new(count);
                for (int index = 0; index < count; index++) {
                    channels.Add(new ModNetworkChannelDescriptor(
                        ReadBoundedString(reader, MaximumPackageNameLength, "package name"),
                        ReadBoundedString(reader, MaximumChannelNameLength, "channel name"),
                        reader.ReadInt32(),
                        (ModNetworkDirection)reader.ReadByte(),
                        reader.ReadInt32()));
                }
                return new ModNetworkChannelCatalog(channels);
            });
        if (!TryCanonicalizeCatalog(catalog, out ModNetworkChannelCatalog canonical, out string error)) {
            throw new InvalidDataException($"Invalid mod channel catalog: {error}");
        }
        return canonical;
    }

    public static byte[] EncodeEnvelope(ModNetworkEnvelope envelope) {
        ValidateEnvelope(envelope);
        return Encode(
            MaximumEnvelopeEncodedBytes,
            writer => {
                writer.Write(EnvelopeWireVersion);
                writer.Write(envelope.MessageId.ToByteArray());
                writer.Write(Normalize(envelope.PackageName));
                writer.Write(Normalize(envelope.ChannelName));
                writer.Write(envelope.ChannelVersion);
                byte[] payload = envelope.Payload ?? [];
                writer.Write(payload.Length);
                writer.Write(payload);
            });
    }

    public static ModNetworkEnvelope DecodeEnvelope(byte[] payload) {
        ModNetworkEnvelope envelope = Decode(
            payload,
            MaximumEnvelopeEncodedBytes,
            reader => {
                byte wireVersion = reader.ReadByte();
                if (wireVersion != EnvelopeWireVersion) {
                    throw new InvalidDataException(
                        $"Unsupported mod message wire version {wireVersion}.");
                }
                byte[] messageIdBytes = reader.ReadBytes(16);
                if (messageIdBytes.Length != 16) {
                    throw new EndOfStreamException("Mod message ID is truncated.");
                }
                Guid messageId = new(messageIdBytes);
                string packageName = ReadBoundedString(reader, MaximumPackageNameLength, "package name");
                string channelName = ReadBoundedString(reader, MaximumChannelNameLength, "channel name");
                int channelVersion = reader.ReadInt32();
                int payloadLength = reader.ReadInt32();
                if (payloadLength < 0 || payloadLength > MaximumPayloadBytes) {
                    throw new InvalidDataException(
                        $"Mod message payload length {payloadLength} is outside 0..{MaximumPayloadBytes}.");
                }
                byte[] messagePayload = reader.ReadBytes(payloadLength);
                if (messagePayload.Length != payloadLength) {
                    throw new EndOfStreamException("Mod message payload is truncated.");
                }
                return new ModNetworkEnvelope(
                    messageId,
                    packageName,
                    channelName,
                    channelVersion,
                    messagePayload);
            });
        ValidateEnvelope(envelope);
        envelope.PackageName = Normalize(envelope.PackageName);
        envelope.ChannelName = Normalize(envelope.ChannelName);
        return envelope;
    }

    public static string ComputeCatalogFingerprint(ModNetworkChannelCatalog catalog) {
        byte[] encoded = EncodeCatalog(catalog);
        return Convert.ToHexString(SHA256.HashData(encoded)).ToLowerInvariant();
    }

    public static string BuildKey(string packageName, string channelName) =>
        $"{Normalize(packageName)}\n{Normalize(channelName)}";

    public static string BuildKey(ModNetworkChannelDescriptor descriptor) =>
        BuildKey(descriptor.PackageName, descriptor.ChannelName);

    private static void ValidateEnvelope(ModNetworkEnvelope envelope) {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.MessageId == Guid.Empty) {
            throw new InvalidDataException("Mod message ID cannot be empty.");
        }
        ModNetworkChannelDescriptor descriptor = new(
            envelope.PackageName,
            envelope.ChannelName,
            envelope.ChannelVersion,
            ModNetworkDirection.Bidirectional,
            Math.Max(1, envelope.Payload?.Length ?? 0));
        if (!TryCanonicalizeDescriptor(descriptor, out _, out string error)) {
            throw new InvalidDataException($"Invalid mod message envelope: {error}");
        }
        if ((envelope.Payload?.Length ?? 0) > MaximumPayloadBytes) {
            throw new InvalidDataException(
                $"Mod message payload exceeds {MaximumPayloadBytes} bytes.");
        }
    }

    private static byte[] Encode(int maximumBytes, Action<BinaryWriter> write) {
        using MemoryStream stream = new();
        using (BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true)) {
            write(writer);
            writer.Flush();
        }
        if (stream.Length > maximumBytes) {
            throw new InvalidDataException($"Mod network payload exceeds {maximumBytes} bytes.");
        }
        return stream.ToArray();
    }

    private static T Decode<T>(
        byte[] payload,
        int maximumBytes,
        Func<BinaryReader, T> read) {
        if (payload == null || payload.Length == 0 || payload.Length > maximumBytes) {
            throw new InvalidDataException(
                $"Mod network payload length must be between 1 and {maximumBytes} bytes.");
        }
        try {
            using MemoryStream stream = new(payload, writable: false);
            using BinaryReader reader = new(stream, Encoding.UTF8, leaveOpen: true);
            T value = read(reader);
            if (stream.Position != stream.Length) {
                throw new InvalidDataException("Mod network payload contains trailing data.");
            }
            return value;
        }
        catch (EndOfStreamException exception) {
            throw new InvalidDataException("Mod network payload is truncated.", exception);
        }
    }

    private static string ReadBoundedString(
        BinaryReader reader,
        int maximumLength,
        string fieldName) {
        string value = reader.ReadString();
        if (value.Length > maximumLength) {
            throw new InvalidDataException(
                $"Mod network {fieldName} exceeds {maximumLength} characters.");
        }
        return value;
    }

    private static int GetEncodedStringLength(string value) {
        int byteCount = Encoding.UTF8.GetByteCount(value);
        int prefixBytes = 1;
        uint remaining = (uint)byteCount;
        while (remaining >= 0x80) {
            prefixBytes++;
            remaining >>= 7;
        }
        return checked(prefixBytes + byteCount);
    }

    private static string Normalize(string value) =>
        value?.Trim().ToLowerInvariant() ?? string.Empty;

    private static bool IsValidIdentifier(string value, int maximumLength) {
        if (value.Length == 0 || value.Length > maximumLength) {
            return false;
        }
        foreach (char character in value) {
            if (!char.IsLetterOrDigit(character)
                && character != '.'
                && character != '-'
                && character != '_') {
                return false;
            }
        }
        return true;
    }

    private static string FormatDescriptor(ModNetworkChannelDescriptor descriptor) =>
        $"v{descriptor.Version}/{descriptor.Directions}/max:{descriptor.MaximumPayloadBytes}";
}

public sealed class ModNetworkTrafficLimiter {
    public const int DefaultMaximumMessagesPerWindow = 128;
    public const int DefaultMaximumBytesPerWindow = 512 * 1024;
    public const int DefaultWindowMilliseconds = 1000;

    private readonly int m_maximumMessages;
    private readonly int m_maximumBytes;
    private readonly int m_windowMilliseconds;
    private long m_windowStartedAt = long.MinValue;
    private int m_messages;
    private int m_bytes;

    public ModNetworkTrafficLimiter(
        int maximumMessages = DefaultMaximumMessagesPerWindow,
        int maximumBytes = DefaultMaximumBytesPerWindow,
        int windowMilliseconds = DefaultWindowMilliseconds) {
        if (maximumMessages <= 0 || maximumBytes <= 0 || windowMilliseconds <= 0) {
            throw new ArgumentOutOfRangeException(
                nameof(maximumMessages),
                "Traffic limiter bounds must be positive.");
        }
        m_maximumMessages = maximumMessages;
        m_maximumBytes = maximumBytes;
        m_windowMilliseconds = windowMilliseconds;
    }

    public bool TryConsume(int payloadBytes, long nowMilliseconds) {
        if (payloadBytes < 0) {
            throw new ArgumentOutOfRangeException(nameof(payloadBytes));
        }
        if (m_windowStartedAt == long.MinValue
            || nowMilliseconds < m_windowStartedAt
            || nowMilliseconds - m_windowStartedAt >= m_windowMilliseconds) {
            m_windowStartedAt = nowMilliseconds;
            m_messages = 0;
            m_bytes = 0;
        }
        if (m_messages >= m_maximumMessages
            || payloadBytes > m_maximumBytes - m_bytes) {
            return false;
        }
        m_messages++;
        m_bytes += payloadBytes;
        return true;
    }

    public void Reset() {
        m_windowStartedAt = long.MinValue;
        m_messages = 0;
        m_bytes = 0;
    }
}

public sealed class ModNetworkMessageDeduplicator {
    public const int DefaultCapacity = 1024;

    private readonly int m_capacity;
    private readonly Queue<Guid> m_order = [];
    private readonly HashSet<Guid> m_seen = [];

    public ModNetworkMessageDeduplicator(int capacity = DefaultCapacity) {
        if (capacity <= 0) {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }
        m_capacity = capacity;
    }

    public bool TryRemember(Guid messageId) {
        if (messageId == Guid.Empty) {
            throw new ArgumentException("Message ID cannot be empty.", nameof(messageId));
        }
        if (!m_seen.Add(messageId)) {
            return false;
        }
        m_order.Enqueue(messageId);
        while (m_order.Count > m_capacity) {
            m_seen.Remove(m_order.Dequeue());
        }
        return true;
    }

    public void Reset() {
        m_order.Clear();
        m_seen.Clear();
    }
}
