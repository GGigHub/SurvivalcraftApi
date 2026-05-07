using Engine;
using GameEntitySystem;

namespace Game.NetWork;

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