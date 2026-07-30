using Engine;
using GameEntitySystem;

namespace Game.Network;

public class DragDropItemPacket : Packet {
    public override byte ID => (byte)PacketType.DragDropItem;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;
    public int fromInventoryId;
    public int fromSlotIndex;
    public int toInventoryId;
    public int toSlotIndex;
    public DragMode dragMode;
    public bool processingOnly;
    public int requestedDragCount;
    public long requestId;

    public DragDropItemPacket() {
    }

    public DragDropItemPacket(IInventory from, int fromIndex, DragMode mode, IInventory to, int toIndex, bool proc, int count) {
        fromInventoryId = from.Id;
        fromSlotIndex = fromIndex;
        dragMode = mode;
        toInventoryId = to.Id;
        toSlotIndex = toIndex;
        processingOnly = proc;
        requestedDragCount = count;
        requestId = NetworkMutationRequestTracker.CreateRequestId();
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref fromInventoryId);
        archive.Value(ref fromSlotIndex);
        archive.Enum(ref dragMode);
        archive.Value(ref toInventoryId);
        archive.Value(ref toSlotIndex);
        archive.Value(ref processingOnly);
        archive.Value(ref requestedDragCount);
        archive.Value(ref requestId);
    }

    public override void Handle(bool isServer) {
        if (!isServer) return;
        var subsystemInventories = GameManager.Project.FindSubsystem<SubsystemInventories>();
        IInventory from = subsystemInventories.GetInventoryById(fromInventoryId);
        IInventory to = subsystemInventories.GetInventoryById(toInventoryId);
        if (from == null || to == null) {
            InventoryPacketHelpers.QueueInventorySync(from, to, From);
            return;
        }
        if (!InventoryPacketHelpers.CanSessionAccessInventory(from, From)
            || !InventoryPacketHelpers.CanSessionAccessInventory(to, From)) {
            InventoryPacketHelpers.QueueInventorySync(from, to, From);
            return;
        }
        if (!NetworkMutationRequestTracker.TryAccept(
                From,
                NetworkMutationDomain.InventoryDragDrop,
                requestId)) {
            InventoryPacketHelpers.QueueInventorySync(from, to, From);
            return;
        }
        if (requestedDragCount <= 0
            || !InventoryPacketHelpers.IsSlotIndexValid(from, fromSlotIndex)
            || !InventoryPacketHelpers.IsSlotIndexValid(to, toSlotIndex)) {
            InventoryPacketHelpers.QueueInventorySync(from, to, From);
            return;
        }
        int sourceSlotValue = from.GetSlotValue(fromSlotIndex);
        int targetSlotValue = to.GetSlotValue(toSlotIndex);
        int sourceSlotCount = from.GetSlotCount(fromSlotIndex);
        int targetSlotCount = to.GetSlotCount(toSlotIndex);
        int targetSlotCapacity = to.GetSlotCapacity(toSlotIndex, sourceSlotValue);
        int targetSlotProcessCapacity = to.GetSlotProcessCapacity(toSlotIndex, sourceSlotValue);

        int actualDragCount = MathUtils.Min(requestedDragCount, sourceSlotCount);

        if (targetSlotProcessCapacity > 0) {
            int processCount = from.RemoveSlotItems(fromSlotIndex, MathUtils.Min(actualDragCount, targetSlotProcessCapacity));
            to.ProcessSlotItems(
                from,
                fromSlotIndex,
                toSlotIndex,
                sourceSlotValue,
                actualDragCount,
                processCount,
                out int processedValue,
                out int processedCount
            );
            if (processedValue != 0
                && processedCount != 0) {
                int count = MathUtils.Min(from.GetSlotCapacity(fromSlotIndex, processedValue), processedCount);
                from.RemoveSlotItems(fromSlotIndex, count);
                from.AddSlotItems(fromSlotIndex, processedValue, count);
            }
        }
        else if (!processingOnly) {
            if ((targetSlotCount == 0 || sourceSlotValue == targetSlotValue) && targetSlotCount < targetSlotCapacity) {
                int num2 = MathUtils.Min(targetSlotCapacity - targetSlotCount, actualDragCount);
                int count2 = from.RemoveSlotItems(fromSlotIndex, num2);
                to.AddSlotItems(toSlotIndex, sourceSlotValue, count2);
            }
            else if (to.GetSlotCapacity(toSlotIndex, sourceSlotValue) >= actualDragCount && from.GetSlotCapacity(fromSlotIndex, targetSlotValue) >= targetSlotCount && sourceSlotCount == actualDragCount) {
                int count3 = to.RemoveSlotItems(toSlotIndex, targetSlotCount);
                int count4 = from.RemoveSlotItems(fromSlotIndex, actualDragCount);
                to.AddSlotItems(toSlotIndex, sourceSlotValue, count4);
                from.AddSlotItems(fromSlotIndex, targetSlotValue, count3);
            }
        }
        InventoryPacketHelpers.QueueInventorySync(from, to);
    }
}

