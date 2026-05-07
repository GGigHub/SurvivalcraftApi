using Engine;
using GameEntitySystem;

namespace Game.Network;

public class DragDropItemPacket : Packet {
    public override byte ID => (byte)PacketType.DragDropItem;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public int fromInventoryId;
    public int fromSlotIndex;
    public int toInventoryId;
    public int toSlotIndex;
    public DragMode dragMode;
    public bool processingOnly;

    public DragDropItemPacket() {
    }

    public DragDropItemPacket(IInventory from, int fromIndex, DragMode mode, IInventory to, int toIndex, bool proc) {
        fromInventoryId = from.Id;
        fromSlotIndex = fromIndex;
        dragMode = mode;
        toInventoryId = to.Id;
        toSlotIndex = toIndex;
        processingOnly = proc;
    }

    public override void Serialize(PacketSerializer archive) {
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
            NetworkManager.QueuePacket(new InventorySyncPacket(from));
            NetworkManager.QueuePacket(new InventorySyncPacket(to));
        }
    }
}

