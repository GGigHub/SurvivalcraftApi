using Game.Network;

namespace Game;

/// <summary>
/// Wire operations reserved for the Enchantment compatibility adapter.
/// </summary>
public enum EnchantmentCompatNetOperation : ushort {
    None = 0,
    RollEnchantment = 1
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
