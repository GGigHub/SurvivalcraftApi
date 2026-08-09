using Engine;
using GameEntitySystem;

namespace Game.Network;

public class ClothingSlotSync {
    public int slotId;
    public List<int> clothes = [];
}

public class ClothingSyncPacket : Packet {
    public const int MaximumSlotCount = 64;
    public const int MaximumClothesPerSlot = 64;
    public const int MaximumComponentTypeNameBytes = 256;

    public override byte ID => (byte)PacketType.ClothingSync;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;
    public int m_inventoryId;
    public int m_entityId;
    public string m_componentTypeName = string.Empty;
    public List<ClothingSlotSync> m_slots = [];
    public long m_lifecycleRevision = -1;
    public long m_revision;

    public ClothingSyncPacket() {
    }

    public ClothingSyncPacket(ComponentClothing clothing, long revision = -1) {
        m_inventoryId = clothing.Id;
        m_entityId = clothing.Entity.Id;
        m_componentTypeName = clothing.GetType().FullName ?? string.Empty;
        m_lifecycleRevision = NetworkEntityStatePacketHelpers.LifecycleRevision(clothing);
        m_revision = NetworkEntityStatePacketHelpers.StateRevision(
            clothing,
            NetworkStateRevisionDomain.Clothing,
            revision);
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
        archive.Value(ref m_componentTypeName, MaximumComponentTypeNameBytes);
        archive.Value(ref m_lifecycleRevision);
        archive.Value(ref m_revision);
        int slotCount = archive.IsWriting ? m_slots.Count : 0;
        archive.Value(ref slotCount);
        if (slotCount is < 0 or > MaximumSlotCount) {
            throw new InvalidDataException(
                $"Invalid clothing slot count {slotCount}; maximum is {MaximumSlotCount}.");
        }
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
        if (clothesCount is < 0 or > MaximumClothesPerSlot) {
            throw new InvalidDataException(
                $"Invalid clothes count {clothesCount}; maximum is {MaximumClothesPerSlot}.");
        }
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
        if (m_inventoryId <= 0
            || m_slots.Count > MaximumSlotCount
            || m_slots.Select(slot => slot.slotId).Distinct().Count() != m_slots.Count
            || m_slots.Any(slot => slot.clothes == null
                || slot.clothes.Count > MaximumClothesPerSlot
                || slot.clothes.Any(value => value < 0))
            || !NetworkEntityStatePacketHelpers.TryResolveComponent(
                isServer,
                m_entityId,
                m_lifecycleRevision,
                m_revision,
                NetworkStateRevisionDomain.Clothing,
                out ComponentClothing clothing)) {
            return;
        }
        if (clothing.Id != m_inventoryId
            || !string.Equals(
                clothing.GetType().FullName,
                m_componentTypeName,
                StringComparison.Ordinal)) return;
        foreach (ClothingSlotSync slotSync in m_slots) {
            if (ClothingSlot.ClothingSlotsByInt.TryGetValue(slotSync.slotId, out ClothingSlot slot)) {
                clothing.SetClothes(slot, slotSync.clothes);
            }
        }
    }
}
