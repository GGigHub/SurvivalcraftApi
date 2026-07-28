using Engine;
using GameEntitySystem;

namespace Game.Network;

public class ClothingSlotSync {
    public int slotId;
    public List<int> clothes = [];
}

public class ClothingSyncPacket : Packet {
    public override byte ID => (byte)PacketType.ClothingSync;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;
    public int m_inventoryId;
    public int m_entityId;
    public string m_componentTypeName = string.Empty;
    public List<ClothingSlotSync> m_slots = [];

    public ClothingSyncPacket() {
    }

    public ClothingSyncPacket(ComponentClothing clothing) {
        m_inventoryId = clothing.Id;
        m_entityId = clothing.Entity.Id;
        m_componentTypeName = clothing.GetType().FullName ?? string.Empty;
        foreach (ClothingSlot slot in ClothingSlot.ClothingSlots.Values.OrderBy(s => s.StableId)) {
            ClothingSlotSync slotSync = new() {
                slotId = slot.StableId,
                clothes = [.. clothing.GetClothes(slot)]
            };
            m_slots.Add(slotSync);
        }
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_inventoryId);
        archive.Value(ref m_entityId);
        archive.Value(ref m_componentTypeName);
        int slotCount = archive.IsWriting ? m_slots.Count : 0;
        archive.Value(ref slotCount);
        if (archive.IsReading) {
            m_slots.Clear();
            for (int i = 0; i < slotCount; i++) {
                ClothingSlotSync slot = new();
                SerializeSlot(archive, slot);
                m_slots.Add(slot);
            }
        }
        else {
            for (int i = 0; i < slotCount; i++) {
                SerializeSlot(archive, m_slots[i]);
            }
        }
    }

    private static void SerializeSlot(PacketSerializer archive, ClothingSlotSync slot) {
        archive.Value(ref slot.slotId);
        int clothesCount = archive.IsWriting ? slot.clothes.Count : 0;
        archive.Value(ref clothesCount);
        if (archive.IsReading) {
            slot.clothes.Clear();
            for (int i = 0; i < clothesCount; i++) {
                int value = 0;
                archive.Value(ref value);
                slot.clothes.Add(value);
            }
        }
        else {
            for (int i = 0; i < clothesCount; i++) {
                int value = slot.clothes[i];
                archive.Value(ref value);
            }
        }
    }

    public override void Handle(bool isServer) {
        if (isServer) return;
        ComponentClothing clothing = ResolveClothing();
        if (clothing == null) {
            return;
        }
        foreach (ClothingSlotSync slotSync in m_slots) {
            if (ClothingSlot.ClothingSlotsByInt.TryGetValue(slotSync.slotId, out ClothingSlot slot)) {
                clothing.SetClothes(slot, slotSync.clothes);
            }
        }
    }

    private ComponentClothing ResolveClothing() {
        if (m_entityId != 0 && !string.IsNullOrEmpty(m_componentTypeName)) {
            Entity entity = GameManager.Project.FindEntity(m_entityId);
            if (entity != null) {
                foreach (ComponentClothing clothing in entity.FindComponents<ComponentClothing>()) {
                    if (clothing.GetType().FullName == m_componentTypeName) {
                        return clothing;
                    }
                }
            }
        }
        IInventory inventory = GameManager.Project.FindSubsystem<SubsystemInventories>().GetInventoryById(m_inventoryId);
        return inventory as ComponentClothing;
    }
}
