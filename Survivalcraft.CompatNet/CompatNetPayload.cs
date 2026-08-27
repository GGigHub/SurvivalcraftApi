using Game.Network;

namespace Game;

/// <summary>
/// Helpers for encoding adapter-specific payloads inside CompatNetPacket.
/// </summary>
public static class CompatNetPayload {
    public static byte[] Write(Action<PacketSerializer> serialize) {
        ArgumentNullException.ThrowIfNull(serialize);

        PacketStreamWriter writer = new();
        serialize(new PacketSerializer(writer));
        return writer.Data();
    }

    public static void Read(byte[] payload, Action<PacketSerializer> deserialize) {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(deserialize);

        using PacketStreamReader reader = new(payload);
        deserialize(new PacketSerializer(reader));
    }
}
