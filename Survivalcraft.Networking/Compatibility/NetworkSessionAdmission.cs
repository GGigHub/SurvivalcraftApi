using System;
using System.IO;
using System.Text;

namespace Game.Network;

public enum NetworkSessionAdmissionState : byte {
    NotStarted,
    AwaitingRequest,
    Accepted,
    Rejected
}

public enum NetworkSessionAdmissionCode : byte {
    None,
    AdmissionRequired,
    AdmissionTimeout,
    InvalidIdentity,
    IdentityChanged,
    Superseded
}

public sealed class NetworkSessionAdmissionResult {
    public bool Accepted;
    public NetworkSessionAdmissionCode Code;
    public string Summary = string.Empty;

    public static NetworkSessionAdmissionResult Accept() => new() {
        Accepted = true,
        Code = NetworkSessionAdmissionCode.None,
        Summary = "Session admission accepted."
    };

    public static NetworkSessionAdmissionResult Reject(
        NetworkSessionAdmissionCode code,
        string summary) {
        if (code == NetworkSessionAdmissionCode.None) {
            throw new ArgumentOutOfRangeException(nameof(code));
        }
        return new NetworkSessionAdmissionResult {
            Accepted = false,
            Code = code,
            Summary = string.IsNullOrWhiteSpace(summary)
                ? "Session admission rejected."
                : summary.Trim()
        };
    }

    public string ToDisplayMessage() => string.IsNullOrWhiteSpace(Summary)
        ? "The server rejected this multiplayer session."
        : Summary;
}

public static class NetworkSessionAdmission {
    public static NetworkSessionAdmissionResult CreateRequiredResult(string packetName) =>
        NetworkSessionAdmissionResult.Reject(
            NetworkSessionAdmissionCode.AdmissionRequired,
            $"Session admission must complete before '{packetName ?? "unknown packet"}' is accepted.");

    public static NetworkSessionAdmissionResult CreateTimeoutResult(int timeoutSeconds) =>
        NetworkSessionAdmissionResult.Reject(
            NetworkSessionAdmissionCode.AdmissionTimeout,
            $"Session admission timed out after {timeoutSeconds} seconds.");

    public static NetworkSessionAdmissionResult CreateInvalidIdentityResult() =>
        NetworkSessionAdmissionResult.Reject(
            NetworkSessionAdmissionCode.InvalidIdentity,
            "The client supplied an empty player identity.");

    public static NetworkSessionAdmissionResult CreateIdentityChangedResult() =>
        NetworkSessionAdmissionResult.Reject(
            NetworkSessionAdmissionCode.IdentityChanged,
            "The client attempted to change identity within an admitted session.");

    public static NetworkSessionAdmissionResult CreateSupersededResult() =>
        NetworkSessionAdmissionResult.Reject(
            NetworkSessionAdmissionCode.Superseded,
            "This connection was superseded by a newer connection for the same player identity.");
}

public static class NetworkSessionAdmissionWireCodec {
    public const int MaximumEncodedBytes = 4096;
    private const int MaximumSummaryLength = 2048;

    public static byte[] Encode(NetworkSessionAdmissionResult result) {
        ArgumentNullException.ThrowIfNull(result);
        string summary = result.Summary ?? string.Empty;
        if (summary.Length > MaximumSummaryLength) {
            throw new InvalidDataException(
                $"Session admission summary exceeds {MaximumSummaryLength} characters.");
        }
        using MemoryStream stream = new();
        using (BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true)) {
            writer.Write(result.Accepted);
            writer.Write((byte)result.Code);
            writer.Write(summary);
            writer.Flush();
        }
        if (stream.Length > MaximumEncodedBytes) {
            throw new InvalidDataException(
                $"Session admission payload exceeds {MaximumEncodedBytes} bytes.");
        }
        return stream.ToArray();
    }

    public static NetworkSessionAdmissionResult Decode(byte[] payload) {
        if (payload == null || payload.Length == 0 || payload.Length > MaximumEncodedBytes) {
            throw new InvalidDataException(
                $"Session admission payload length must be between 1 and {MaximumEncodedBytes} bytes.");
        }
        try {
            using MemoryStream stream = new(payload, writable: false);
            using BinaryReader reader = new(stream, Encoding.UTF8, leaveOpen: true);
            bool accepted = reader.ReadBoolean();
            NetworkSessionAdmissionCode code = (NetworkSessionAdmissionCode)reader.ReadByte();
            string summary = reader.ReadString();
            if (summary.Length > MaximumSummaryLength) {
                throw new InvalidDataException(
                    $"Session admission summary exceeds {MaximumSummaryLength} characters.");
            }
            if (stream.Position != stream.Length) {
                throw new InvalidDataException("Session admission payload contains trailing data.");
            }
            if (!Enum.IsDefined(code) || accepted != (code == NetworkSessionAdmissionCode.None)) {
                throw new InvalidDataException("Session admission result is internally inconsistent.");
            }
            return new NetworkSessionAdmissionResult {
                Accepted = accepted,
                Code = code,
                Summary = summary
            };
        }
        catch (EndOfStreamException exception) {
            throw new InvalidDataException("Session admission payload is truncated.", exception);
        }
    }
}
