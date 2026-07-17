using Engine;
using GameEntitySystem;
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
        m_fromPlayerIndex = playerData.PlayerIndex;
        m_inventoryId = inventoryId;
        m_slotIndex = slotIndex;
        m_hitPosition = position;
        m_count = count;
    }
    public override void Serialize(PacketSerializer archive) {
        SerializePlayerId(archive, archive.IsWriting ? ResolvePlayer(m_fromPlayerIndex) : null);
        archive.Value(ref m_inventoryId);
        archive.Value(ref m_slotIndex);
        archive.Value(ref m_hitPosition);
        archive.Value(ref m_count);
    }

    public override void Handle(bool isServer) {
        if (!TryResolvePlayer(isServer, out PlayerData playerData)) return;
        GameManager.Project.FindSubsystem<SubsystemInventories>().FindInventoryById(m_inventoryId, inventory => {
            if (inventory == null || IsOwnedByOtherPlayer(inventory, From, out _)) {
                return;
            }
            if (inventory is ComponentCreativeInventory && GameManager.Project.FindSubsystem<SubsystemGameInfo>()?.WorldSettings?.GameMode != GameMode.Creative) {
                //NetworkManager.Server.RemoveClient(From, "行为异常");
                return;
            }
            if (!IsSlotIndexValid(inventory, m_slotIndex)) {
                if (isServer && From != null) {
                    InventoryPacketHelpers.QueueInventorySync(inventory, From);
                }
                return;
            }
            playerData.ComponentPlayer.ViewWidget.DragDrop(m_hitPosition, new InventoryDragData { Inventory = inventory, SlotIndex = m_slotIndex }, m_count, true);
        });
    }

    public static bool IsOwnedByOtherPlayer(IInventory inventory, ClientSession from, out ComponentPlayer owner) {
        owner = null;
        if (inventory is Component component) {
            owner = component.Entity?.FindComponent<ComponentPlayer>();
            if (owner?.PlayerData != null && from != null) {
                return owner.PlayerData.PlayerIndex != from.PlayerIndex;
            }
        }
        return false;
    }

    public static bool IsSlotIndexValid(IInventory inventory, int index) {
        return InventoryPacketHelpers.IsSlotIndexValid(inventory, index);
    }
}


