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
    public const int MaximumSlotCount = 4096;

    public override byte ID => (byte)PacketType.InventorySync;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;
    public int m_inventoryId;
    public int m_entityId;
    public string m_inventoryComponentTypeName = string.Empty;
    public int m_activeSlotIndex;
    public long m_lifecycleRevision = -1;
    public long m_revision;
    public List<InventorySlot> m_slots = new List<InventorySlot>();

    public InventorySyncPacket() {
    }

    public InventorySyncPacket(IInventory inventory) {
        m_inventoryId = inventory.Id;
        m_activeSlotIndex = inventory.ActiveSlotIndex;
        m_lifecycleRevision = InventoryPacketHelpers.GetLifecycleRevision(inventory);
        m_revision = NetworkStateRevisionTracker.GetAuthoritativeRevision(
            inventory.Project,
            NetworkStateRevisionDomain.Inventory,
            inventory.Id);
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
        archive.Value(ref m_activeSlotIndex);
        archive.Value(ref m_lifecycleRevision);
        archive.Value(ref m_revision);
        int count = archive.IsWriting ? m_slots.Count : 0;
        archive.Value(ref count);
        if (count is < 0 or > MaximumSlotCount) {
            throw new InvalidDataException($"Invalid inventory slot count {count}.");
        }
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
        Project project = GameManager.Project;
        if (project == null || m_inventoryId <= 0 || m_revision < 0) {
            return;
        }
        if (m_entityId > 0
            && !EntityLifecycleTracker.CanApplyState(
                project,
                m_entityId,
                m_lifecycleRevision)) {
            if (EntityLifecycleTracker.ShouldRequestLoad(
                project,
                m_entityId,
                m_lifecycleRevision,
                project.FindEntity(m_entityId) != null)) {
                NetworkManager.Queue(new EntitySyncRequestPacket([m_entityId]));
            }
            return;
        }
        IInventory inventory = ResolveInventory();
        if (inventory != null) {
            if (inventory is ComponentClothing) {
                return;
            }
            if (m_slots.Count != inventory.SlotsCount
                || m_slots.Select(slot => slot.slotIndex).Distinct().Count() != m_slots.Count
                || m_slots.Any(slot => slot.slotIndex < 0
                    || slot.slotIndex >= inventory.SlotsCount
                    || slot.count < 0)) {
                return;
            }
            NetworkStateRevisionDecision decision = NetworkStateRevisionTracker.Receive(
                project,
                NetworkStateRevisionDomain.Inventory,
                m_inventoryId,
                m_revision,
                forceRefresh: true);
            if (!decision.Apply) {
                return;
            }
            foreach (var slot in m_slots) {
                inventory.RemoveSlotItems(slot.slotIndex, inventory.GetSlotCount(slot.slotIndex));
                inventory.AddSlotItems(slot.slotIndex, slot.value, slot.count);
            }
            InventoryPacketHelpers.ApplyAuthoritativeActiveSlot(
                inventory,
                m_activeSlotIndex);
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

