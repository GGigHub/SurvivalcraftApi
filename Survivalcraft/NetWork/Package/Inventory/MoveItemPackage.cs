using Engine;
using GameEntitySystem;

namespace Game.NetWork;

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