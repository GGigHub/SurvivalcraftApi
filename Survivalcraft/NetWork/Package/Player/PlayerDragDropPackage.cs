using Engine;
using TemplatesDatabase;

namespace Game.NetWork;

public class PlayerDragDropPackage : PlayerPackageBase {
    public override byte ID => (byte)PackageType.PlayerDragDrop;
    public int m_inventoryId;
    public int m_slotIndex;
    public Vector3 m_hitPosition;
    public int m_count;
    public PlayerDragDropPackage() { }
    public PlayerDragDropPackage(PlayerData playerData, int inventoryId, int slotIndex, Vector3 position, int count) {
        m_fromPlayerId = playerData?.ClientId ?? 0;
        m_inventoryId = inventoryId;
        m_slotIndex = slotIndex;
        m_hitPosition = position;
        m_count = count;
    }
    public override void Serialize(PackageSerializer archive) {
        SerializePlayerId(archive, archive.IsWriting ? PlayerPackageHelpers.ResolvePlayer(m_fromPlayerId) : null);
        archive.Value(ref m_inventoryId);
        archive.Value(ref m_slotIndex);
        archive.Value(ref m_hitPosition);
        archive.Value(ref m_count);
    }
    public override void Handle(bool isServer) {
        if (!TryResolvePlayer(isServer, out PlayerData playerData)) return;
        GameManager.Project.FindSubsystem<SubsystemInventories>().FindInventoryById(m_inventoryId, inventory => {
            if (inventory == null || InventoryAccessGuard.IsOwnedByOtherPlayer(inventory, From, out _)) {
                return;
            }
            if (inventory is ComponentCreativeInventory && NetworkManager.GetEffectiveGameMode(From.PlayerData) != GameMode.Creative) {
                NetworkManager.Server.RemoveClient(From.PlayerData.Client, "闈炴硶鎿嶄綔: 鐢熷瓨妯″紡浣跨敤鍒涢€犵墿鍝?");
                return;
            }
            if (!InventoryAccessGuard.IsSlotIndexValid(inventory, m_slotIndex)) {
                if (isServer && From != null) {
                    NetworkManager.QueuePackage(new InventorySyncPackage(inventory) { To = From });
                }
                return;
            }
            playerData.ComponentPlayer.ViewWidget.NetDragDrop(m_hitPosition, new InventoryDragData { Inventory = inventory, SlotIndex = m_slotIndex }, m_count);
        });
    }
}