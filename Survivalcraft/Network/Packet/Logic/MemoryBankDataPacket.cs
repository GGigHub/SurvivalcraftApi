using GameEntitySystem;

namespace Game.Network;

public class MemoryBankDataPacket : EditableBlockPacketBase {
    public override byte ID => (byte)PacketType.EditableMemoryBank;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;
    private bool m_syncItem;
    private bool m_editAsItem;
    private int m_slotIndex;
    private int m_inventoryId;
    private int? m_id;
    private CellFace m_cellFace;
    private byte[] m_data;

    public MemoryBankDataPacket() { }
    public MemoryBankDataPacket(CellFace cell, bool editAsItem, int inventoryId, int slotIndex, MemoryBankData data) {
        m_cellFace = cell;
        m_editAsItem = editAsItem;
        m_inventoryId = inventoryId;
        m_slotIndex = slotIndex;
        m_data = data.Data.ToArray();
    }
    public MemoryBankDataPacket(int id, MemoryBankData data) {
        m_syncItem = true;
        m_slotIndex = id;
        m_data = data.Data.ToArray();
    }
    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_syncItem);
        if (m_syncItem) {
            archive.Value(ref m_slotIndex);
        }
        else {
            SerializeEditableTarget(archive, ref m_editAsItem, ref m_inventoryId, ref m_slotIndex, ref m_cellFace);
            SerializeOptionalId(archive, ref m_id);
        }
        SerializeByteArray(archive, ref m_data);
    }
    public override void Handle(bool isServer) {
        SubsystemMemoryBankBlockBehavior behavior = GameManager.Project.FindSubsystem<SubsystemMemoryBankBlockBehavior>();
        MemoryBankData data = new();
        foreach (byte item in m_data) data.Data.Add(item);
        if (m_syncItem) {
            behavior.m_itemsData[m_slotIndex] = data;
        }
        else if (m_editAsItem) {
            GameManager.Project.FindSubsystem<SubsystemInventories>().FindInventoryById(m_inventoryId, inventory => {
                if (!m_id.HasValue) {
                    m_id = behavior.StoreItemDataAtUniqueId(data);
                    ReplaceDataAtSlot(inventory, m_slotIndex, _ => m_id.Value);
                }
                else {
                    behavior.m_itemsData[m_id.Value] = data;
                }
            });
        }
        else {
            behavior.SetBlockData(m_cellFace.Point, data);
        }
        if (isServer) NetworkManager.Queue(this);
    }
}

