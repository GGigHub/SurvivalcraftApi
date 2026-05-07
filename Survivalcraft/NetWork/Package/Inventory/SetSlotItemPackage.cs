using Engine;
using GameEntitySystem;

namespace Game.NetWork;

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