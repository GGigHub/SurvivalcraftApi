using Game.Network;

namespace Game;

/// <summary>
/// The only MCHszr operations currently accepted on the wire. The payload
/// carries intent and a slot identity; it never carries damage, velocity,
/// target IDs, or a client-computed result.
/// </summary>
public enum MCHszrCompatNetOperation : ushort {
    PoseToggle = 1,
    ChargeBegin = 2,
    ChargeRelease = 3,
    FoodConsume = 4
}

public enum MCHszrCompatNetAction : byte {
    SpearDash = 1,
    HammerShockwave = 2
}

public sealed class MCHszrCompatNetRequest {
    public MCHszrCompatNetAction Action;
    public int EntityId;
    public int SlotIndex;
    public int ItemValue;
    public int Sequence;

    public void Serialize(PacketSerializer archive) {
        archive.Enum(ref Action);
        archive.Value(ref EntityId);
        archive.Value(ref SlotIndex);
        archive.Value(ref ItemValue);
        archive.Value(ref Sequence);
    }
}
