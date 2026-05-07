using Engine;
using GameEntitySystem;

namespace Game.NetWork;

public class InventorySyncPackage : IPackage {
    public override byte ID => (byte)PackageType.InventorySync;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public int m_inventoryId;
    public List<InventorySlot> m_slots = new List<InventorySlot>();

    public InventorySyncPackage() {
    }

    public InventorySyncPackage(IInventory inventory) {
        m_inventoryId = inventory.Id;
        for (int i = 0; i < inventory.SlotsCount; i++) {
            m_slots.Add(new InventorySlot {
                inventoryId = m_inventoryId,
                slotIndex = i,
                value = inventory.GetSlotValue(i),
                count = inventory.GetSlotCount(i)
            });
        }
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_inventoryId);
        int count = archive.IsWriting ? m_slots.Count : 0;
        archive.Value(ref count);
        if (archive.IsReading) {
            m_slots.Clear();
            for (int i = 0; i < count; i++) {
                InventorySlot slot = new InventorySlot();
                archive.Value(ref slot.slotIndex);
                archive.Value(ref slot.value);
                archive.Value(ref slot.count);
                m_slots.Add(slot);
            }
        }
        else {
            foreach (var slot in m_slots) {
                archive.Value(ref slot.slotIndex);
                archive.Value(ref slot.value);
                archive.Value(ref slot.count);
            }
        }
    }

    public override void Handle(bool isServer) {
        if (isServer) return;
        var subsystemInventories = GameManager.Project.FindSubsystem<SubsystemInventories>();
        IInventory inventory = subsystemInventories.GetInventoryById(m_inventoryId);
        if (inventory != null) {
            foreach (var slot in m_slots) {
                inventory.RemoveSlotItems(slot.slotIndex, inventory.GetSlotCount(slot.slotIndex));
                inventory.AddSlotItems(slot.slotIndex, slot.value, slot.count);
            }
        }
    }
}