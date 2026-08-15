using Engine;

namespace Game.Network;

/// <summary>
/// 客户端请求服务端执行创造模式的键取方块。
/// 方块值由服务端根据射线重新计算，客户端只提供视线方向和期望槽位。
/// </summary>
public sealed class PlayerPickBlockPacket : PlayerPacketBase {
    public override byte ID => (byte)PacketType.PlayerPickBlock;

    public Ray3 m_pickRay;
    public int m_slotIndex;

    public PlayerPickBlockPacket() { }

    public PlayerPickBlockPacket(ComponentPlayer player, Ray3 ray, int slotIndex) {
        m_fromPlayerIndex = player.PlayerData.PlayerIndex;
        m_pickRay = ray;
        m_slotIndex = slotIndex;
    }

    public override void Serialize(PacketSerializer archive) {
        SerializePlayerId(archive, archive.IsWriting ? ResolvePlayer(m_fromPlayerIndex) : null);
        archive.Value(ref m_pickRay);
        archive.Value(ref m_slotIndex);
    }

    public override void Handle(bool isServer) {
        if (!isServer
            || !TryResolvePlayer(true, out PlayerData playerData)
            || playerData.ComponentPlayer == null
            || playerData.ComponentPlayer.m_subsystemGameInfo.WorldSettings.GameMode != GameMode.Creative
            || m_pickRay.Direction.LengthSquared() < 1E-06f) {
            return;
        }

        ComponentPlayer player = playerData.ComponentPlayer;
        Ray3 serverRay = new(
            player.ComponentCreatureModel.EyePosition,
            Vector3.Normalize(m_pickRay.Direction)
        );
        if (!player.TryPickBlock(
                serverRay,
                out _,
                displayFeedback: false,
                preferredSlotIndex: m_slotIndex
            )) {
            return;
        }

        IInventory inventory = player.ComponentMiner?.Inventory;
        if (inventory == null) {
            return;
        }
        InventoryPacketHelpers.QueueInventorySync(inventory, From);
        NetworkManager.Queue(new ActiveSlotChangePacket(inventory) { To = From });
    }
}
