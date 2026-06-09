using Engine;
using GameEntitySystem;

namespace Game.Network;

public class ActiveSlotChangePacket : Packet {
    public override byte ID => (byte)PacketType.ActiveSlotChange;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;
    public int m_inventoryId;
    public int m_activeSlotIndex;

    public ActiveSlotChangePacket() {
    }

    public ActiveSlotChangePacket(IInventory inventory) {
        m_inventoryId = inventory.Id;
        m_activeSlotIndex = inventory.ActiveSlotIndex;
    }

    public override void Serialize(PacketSerializer archive) {
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
                NetworkService.QueuePacket(this);
            }
        }
    }
}

