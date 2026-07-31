using System;
using System.Net;
using LiteNetLib.Utils;

namespace Game.Network;

public sealed class RelayCandidateInfo {
    public string RelayId { get; init; }
    public string Host { get; init; }
    public int UdpPort { get; init; }
    public string Region { get; init; }
    public string Version { get; init; }
    public string Status { get; init; } = "Active";
    public int Priority { get; init; }
    public int RoomCount { get; init; }
    public int PeerCount { get; init; }
    public int LastSeenSecondsAgo { get; init; }
    public bool IsBusy => string.Equals(Status, "Busy", StringComparison.OrdinalIgnoreCase);
}

public enum RelayMessageType : byte {
    CreateRoom = 1,
    RoomCreated = 2,
    JoinRoom = 3,
    JoinAccepted = 4,
    JoinRejected = 5,
    PeerJoined = 6,
    PeerLeft = 7,
    DataToHost = 8,
    DataToPeer = 9,
    CloseRoom = 10
}

public static class RelayProtocol {
    public const uint Magic = 0x5343524C; // SCRL
    public const byte Version = 1;
    public const string ConnectionKey = "x26.08.01";
    public const int MaxPayloadLength = 64 * 1024;

    public static NetDataWriter Begin(RelayMessageType type) {
        NetDataWriter writer = new();
        writer.Put(Magic);
        writer.Put(Version);
        writer.Put((byte)type);
        return writer;
    }

    public static bool TryReadHeader(NetDataReader reader, out RelayMessageType type) {
        type = default;
        if (reader == null || reader.AvailableBytes < 6 || reader.GetUInt() != Magic || reader.GetByte() != Version) return false;
        type = (RelayMessageType)reader.GetByte();
        return true;
    }
}
