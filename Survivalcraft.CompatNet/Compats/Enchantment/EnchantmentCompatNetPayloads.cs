using Game.Network;

namespace Game;

/// <summary>
/// Wire operations reserved for the Enchantment compatibility adapter.
/// </summary>
public enum EnchantmentCompatNetOperation : ushort {
    None = 0,
    RollEnchantment = 1,
    RemoveEnchantingTableResult = 2
}

public static class EnchantmentCompatNetPayloads {
    public static byte[] Empty() => [];
}

public sealed class EnchantmentRollRequest {
    public int EntityId;

    public void Serialize(PacketSerializer archive) {
        archive.Value(ref EntityId);
    }
}

/// <summary>
/// Requests taking the result from the Enchantment mod's enchanting table.
/// The client supplies only the entity and the fixed result slot/count; the
/// server re-checks the table contents, player and distance before invoking
/// the original inventory method.
/// </summary>
public sealed class EnchantingTableResultRequest {
    public int EntityId;

    public int SlotIndex;

    public int Count;

    public void Serialize(PacketSerializer archive) {
        archive.Value(ref EntityId);
        archive.Value(ref SlotIndex);
        archive.Value(ref Count);
    }
}
