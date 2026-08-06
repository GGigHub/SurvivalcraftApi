using Engine;

namespace Game.Network;

public class CrossbowPullPacket : Packet {
    public override byte ID => (byte)PacketType.CrossbowPull;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;

    public int m_inventoryId;
    public int m_slotIndex;

    public CrossbowPullPacket() {
    }

    public CrossbowPullPacket(IInventory inventory, int slotIndex) {
        m_inventoryId = inventory.Id;
        m_slotIndex = slotIndex;
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_inventoryId);
        archive.Value(ref m_slotIndex);
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            var subsystemInventories = GameManager.Project.FindSubsystem<SubsystemInventories>();
            IInventory inventory = subsystemInventories.GetInventoryById(m_inventoryId);
            if (inventory != null) {
                int theItemValue = inventory.GetSlotValue(m_slotIndex);
                if (Terrain.ExtractContents(theItemValue) == CrossbowBlock.Index) {
                    int data = Terrain.ExtractData(theItemValue);
                    int value = Terrain.MakeBlockValue(CrossbowBlock.Index, 0, CrossbowBlock.SetDraw(data, 15));
                    inventory.SetSlotValue(m_slotIndex, new ComponentInventoryBase.Slot {
                        Value = value,
                        Count = inventory.GetSlotCount(m_slotIndex)
                    });
                    InventoryPacketHelpers.QueueInventorySync(inventory);
                }
            }
        }
    }
}

