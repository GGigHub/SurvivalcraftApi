using Engine;
using GameEntitySystem;

namespace Game.Network;

public class MoveItemPacket : Packet {
    public override byte ID => (byte)PacketType.MoveItem;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;
    public int fromInventoryId;
    public int fromSlotIndex;
    public int toInventoryId;
    public int toSlotIndex;
    public int count;

    public MoveItemPacket() {
    }

    public MoveItemPacket(IInventory from, int fromIndex, IInventory to, int toIndex, int c) {
        fromInventoryId = from.Id;
        fromSlotIndex = fromIndex;
        toInventoryId = to.Id;
        toSlotIndex = toIndex;
        count = c;
    }

    public override void Serialize(PacketSerializer archive) {
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
        if (from == null || to == null) {
            InventoryPacketHelpers.QueueInventorySync(from, to, From);
            return;
        }
        if (from != null && to != null) {
            if (PlayerDragDropPacket.IsOwnedByOtherPlayer(from, From, out _)
                || PlayerDragDropPacket.IsOwnedByOtherPlayer(to, From, out _)) {
                InventoryPacketHelpers.QueueInventorySync(from, to, From);
                return;
            }
            if (count <= 0
                || !InventoryPacketHelpers.IsSlotIndexValid(from, fromSlotIndex)
                || !InventoryPacketHelpers.IsSlotIndexValid(to, toSlotIndex)) {
                InventoryPacketHelpers.QueueInventorySync(from, to, From);
                return;
            }

            int value = from.GetSlotValue(fromSlotIndex);
            int targetValue = to.GetSlotValue(toSlotIndex);
            int sourceCount = from.GetSlotCount(fromSlotIndex);
            int targetCount = to.GetSlotCount(toSlotIndex);
            int targetCapacity = to.GetSlotCapacity(toSlotIndex, value);
            int actualCount = 0;
            if (targetCount == 0 || value == targetValue) {
                actualCount = MathUtils.Min(targetCapacity - targetCount, sourceCount, count);
            }
            if (actualCount > 0) {
                int removedCount = from.RemoveSlotItems(fromSlotIndex, actualCount);
                to.AddSlotItems(toSlotIndex, value, removedCount);
            }
            InventoryPacketHelpers.QueueInventorySync(from, to);
        }
    }
}

