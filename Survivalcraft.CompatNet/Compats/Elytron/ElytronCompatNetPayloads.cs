using Game.Network;

namespace Game;

/// <summary>
/// Input intents understood by the Elytron CompatNet adapter.
/// The operation is the intent; the payload is deliberately empty so a client
/// cannot select another entity, position, velocity, or inventory slot.
/// </summary>
public enum ElytronCompatNetOperation : ushort {
    None = 0,
    JumpPressed = 1,
    FireworkIntent = 2
}

/// <summary>
/// Payload helpers for the Elytron input-intent protocol.
/// </summary>
public static class ElytronCompatNetPayloads {
}

public sealed class ElytronJumpPressedRequest {
    public int Sequence;

    public void Serialize(PacketSerializer archive) {
        archive.Value(ref Sequence);
    }
}

public sealed class ElytronFireworkIntentRequest {
    public int Sequence;

    public void Serialize(PacketSerializer archive) {
        archive.Value(ref Sequence);
    }
}
