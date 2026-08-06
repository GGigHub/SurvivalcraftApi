using Engine;
using GameEntitySystem;

namespace Game.Network;

public class InventorySlot {
    public int inventoryId;
    public int slotIndex;
    public int value;
    public int count;
}

public class InventorySyncPacket : Packet {
    public override byte ID => (byte)PacketType.InventorySync;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;
    public int m_inventoryId;
    public int m_entityId;
    public string m_inventoryComponentTypeName = string.Empty;
    public List<InventorySlot> m_slots = new List<InventorySlot>();

    public InventorySyncPacket() {
    }

    public InventorySyncPacket(IInventory inventory) {
        m_inventoryId = inventory.Id;
        if (inventory is Component component) {
            m_entityId = component.Entity.Id;
            m_inventoryComponentTypeName = component.GetType().FullName ?? string.Empty;
        }
        for (int i = 0; i < inventory.SlotsCount; i++) {
            m_slots.Add(new InventorySlot {
                inventoryId = m_inventoryId,
                slotIndex = i,
                value = inventory.GetSlotValue(i),
                count = inventory.GetSlotCount(i)
            });
        }
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_inventoryId);
        archive.Value(ref m_entityId);
        archive.Value(ref m_inventoryComponentTypeName);
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
        IInventory inventory = ResolveInventory();
        if (inventory != null) {
            if (inventory is ComponentClothing) {
                return;
            }
            foreach (var slot in m_slots) {
                InventoryPacketHelpers.ApplySlotValue(
                    inventory,
                    slot.slotIndex,
                    slot.value,
                    slot.count
                );
            }
        }
    }

    IInventory ResolveInventory() {
        if (m_entityId != 0 && !string.IsNullOrEmpty(m_inventoryComponentTypeName)) {
            Entity entity = GameManager.Project.FindEntity(m_entityId);
            if (entity != null) {
                foreach (IInventory inventory in entity.FindComponents<IInventory>()) {
                    if (inventory.GetType().FullName == m_inventoryComponentTypeName) {
                        return inventory;
                    }
                }
            }
        }
        var subsystemInventories = GameManager.Project.FindSubsystem<SubsystemInventories>();
        return subsystemInventories.GetInventoryById(m_inventoryId);
    }
}

