using Engine;
using TemplatesDatabase;

namespace Game.Network;

public class PlayerDragDropPacket : PlayerPacketBase {
    public override byte ID => (byte)PacketType.PlayerDragDrop;
    public int m_inventoryId;
    public int m_slotIndex;
    public Vector3 m_hitPosition;
    public int m_count;
    public PlayerDragDropPacket() { }
    public PlayerDragDropPacket(PlayerData playerData, int inventoryId, int slotIndex, Vector3 position, int count) {
        m_fromPlayerId = playerData?.ClientId ?? 0;
        m_inventoryId = inventoryId;
        m_slotIndex = slotIndex;
        m_hitPosition = position;
        m_count = count;
    }
    public override void Serialize(PacketSerializer archive) {
        SerializePlayerId(archive, archive.IsWriting ? PlayerHelpers.ResolvePlayer(m_fromPlayerId) : null);
        archive.Value(ref m_inventoryId);
        archive.Value(ref m_slotIndex);
        archive.Value(ref m_hitPosition);
        archive.Value(ref m_count);
    }
    public override void Handle(bool isServer) {
        if (!TryResolvePlayer(isServer, out PlayerData playerData)) return;
        GameManager.Project.FindSubsystem<SubsystemInventories>().FindInventoryById(m_inventoryId, inventory => {
            if (inventory == null || InventoryHelper.IsOwnedByOtherPlayer(inventory, From, out _)) {
                return;
            }
            if (inventory is ComponentCreativeInventory && NetworkManager.GetEffectiveGameMode(From.PlayerData) != GameMode.Creative) {
                NetworkManager.Server.RemoveClient(From.PlayerData.Client, "闈炴硶鎿嶄綔: 鐢熷瓨妯″紡浣跨敤鍒涢€犵墿�?");
                return;
            }
            if (!InventoryHelper.IsSlotIndexValid(inventory, m_slotIndex)) {
                if (isServer && From != null) {
                    NetworkManager.QueuePacket(new InventorySyncPacket(inventory) { To = From });
                }
                return;
            }
            playerData.ComponentPlayer.ViewWidget.NetDragDrop(m_hitPosition, new InventoryDragData { Inventory = inventory, SlotIndex = m_slotIndex }, m_count);
        });
    }
}

