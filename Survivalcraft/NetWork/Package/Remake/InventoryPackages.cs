using Engine;
using GameEntitySystem;

namespace Game.NetWork.Packages;

public class ActiveSlotChangePackage : IPackage {
    public override byte ID => (byte)PackageType.ActiveSlotChange;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public int m_inventoryId;
    public int m_activeSlotIndex;

    public ActiveSlotChangePackage() {
    }

    public ActiveSlotChangePackage(IInventory inventory) {
        m_inventoryId = inventory.Id;
        m_activeSlotIndex = inventory.ActiveSlotIndex;
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_inventoryId);
        archive.Value(ref m_activeSlotIndex);
    }

    public override void Handle(bool isServer) {
        var subsystemInventories = GameManager.Project.FindSubsystem<SubsystemInventories>();
        IInventory inventory = subsystemInventories.GetInventoryById(m_inventoryId);
        if (inventory != null) {
            inventory.ActiveSlotIndex = m_activeSlotIndex;
            if (isServer) {
                To = From;
                NetworkManager.QueuePackage(this);
            }
        }
    }
}

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

public class MoveItemPackage : IPackage {
    public override byte ID => (byte)PackageType.MoveItem;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public int fromInventoryId;
    public int fromSlotIndex;
    public int toInventoryId;
    public int toSlotIndex;
    public int count;

    public MoveItemPackage() {
    }

    public MoveItemPackage(IInventory from, int fromIndex, IInventory to, int toIndex, int c) {
        fromInventoryId = from.Id;
        fromSlotIndex = fromIndex;
        toInventoryId = to.Id;
        toSlotIndex = toIndex;
        count = c;
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref fromInventoryId);
        archive.Value(ref fromSlotIndex);
        archive.Value(ref toInventoryId);
        archive.Value(ref toSlotIndex);
        archive.Value(ref count);
    }

    public override void Handle(bool isServer) {
        if (!isServer) return;
        var subsystemInventories = GameManager.Project.FindSubsystem<SubsystemInventories>();
        IInventory from = subsystemInventories.GetInventoryById(fromInventoryId);
        IInventory to = subsystemInventories.GetInventoryById(toInventoryId);
        if (from != null && to != null) {
            int value = from.GetSlotValue(fromSlotIndex);
            int actualCount = MathUtils.Min(count, from.GetSlotCount(fromSlotIndex));
            from.RemoveSlotItems(fromSlotIndex, actualCount);
            to.AddSlotItems(toSlotIndex, value, actualCount);
            NetworkManager.QueuePackage(new InventorySyncPackage(from));
            NetworkManager.QueuePackage(new InventorySyncPackage(to));
        }
    }
}

public class SetSlotItemPackage : IPackage {
    public override byte ID => (byte)PackageType.SetSlotItem;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public int m_inventoryId;
    public int m_slotIndex;
    public int m_value;
    public int m_count;

    public SetSlotItemPackage() {
    }

    public SetSlotItemPackage(IInventory inventory, int index, int value, int count) {
        m_inventoryId = inventory.Id;
        m_slotIndex = index;
        m_value = value;
        m_count = count;
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_inventoryId);
        archive.Value(ref m_slotIndex);
        archive.Value(ref m_value);
        archive.Value(ref m_count);
    }

    public override void Handle(bool isServer) {
        var subsystemInventories = GameManager.Project.FindSubsystem<SubsystemInventories>();
        IInventory inventory = subsystemInventories.GetInventoryById(m_inventoryId);
        if (inventory != null) {
            inventory.RemoveSlotItems(m_slotIndex, inventory.GetSlotCount(m_slotIndex));
            inventory.AddSlotItems(m_slotIndex, m_value, m_count);
            if (isServer) {
                To = From;
                NetworkManager.QueuePackage(this);
            }
        }
    }
}

public class DragDropItemPackage : IPackage {
    public override byte ID => (byte)PackageType.DragDropItem;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public int fromInventoryId;
    public int fromSlotIndex;
    public int toInventoryId;
    public int toSlotIndex;
    public DragMode dragMode;
    public bool processingOnly;

    public DragDropItemPackage() {
    }

    public DragDropItemPackage(IInventory from, int fromIndex, DragMode mode, IInventory to, int toIndex, bool proc) {
        fromInventoryId = from.Id;
        fromSlotIndex = fromIndex;
        dragMode = mode;
        toInventoryId = to.Id;
        toSlotIndex = toIndex;
        processingOnly = proc;
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref fromInventoryId);
        archive.Value(ref fromSlotIndex);
        archive.Enum(ref dragMode);
        archive.Value(ref toInventoryId);
        archive.Value(ref toSlotIndex);
        archive.Value(ref processingOnly);
    }

    public override void Handle(bool isServer) {
        if (!isServer) return;
        var subsystemInventories = GameManager.Project.FindSubsystem<SubsystemInventories>();
        IInventory from = subsystemInventories.GetInventoryById(fromInventoryId);
        IInventory to = subsystemInventories.GetInventoryById(toInventoryId);
        if (from != null && to != null) {
            int sourceSlotValue = from.GetSlotValue(fromSlotIndex);
            int targetSlotValue = to.GetSlotValue(toSlotIndex);
            int dragCount = from.GetSlotCount(fromSlotIndex);
            int targetSlotCount = to.GetSlotCount(toSlotIndex);
            int targetSlotCapacity = to.GetSlotCapacity(toSlotIndex, sourceSlotValue);
            
            int actualDragCount = dragCount;
            if (dragMode == DragMode.SingleItem) actualDragCount = MathUtils.Min(dragCount, 1);
            else if (dragMode == DragMode.HalfItems) actualDragCount = (dragCount + 1) / 2;

            if (!processingOnly) {
                 if ((targetSlotCount == 0 || sourceSlotValue == targetSlotValue) && targetSlotCount < targetSlotCapacity) {
                    int num2 = MathUtils.Min(targetSlotCapacity - targetSlotCount, actualDragCount);
                    int count2 = from.RemoveSlotItems(fromSlotIndex, num2);
                    to.AddSlotItems(toSlotIndex, sourceSlotValue, count2);
                }
                else if (to.GetSlotCapacity(toSlotIndex, sourceSlotValue) >= actualDragCount && from.GetSlotCapacity(fromSlotIndex, targetSlotValue) >= targetSlotCount && from.GetSlotCount(fromSlotIndex) == actualDragCount) {
                    int count3 = to.RemoveSlotItems(toSlotIndex, targetSlotCount);
                    int count4 = from.RemoveSlotItems(fromSlotIndex, actualDragCount);
                    to.AddSlotItems(toSlotIndex, sourceSlotValue, count4);
                    from.AddSlotItems(fromSlotIndex, targetSlotValue, count3);
                }
            }
            NetworkManager.QueuePackage(new InventorySyncPackage(from));
            NetworkManager.QueuePackage(new InventorySyncPackage(to));
        }
    }
}
