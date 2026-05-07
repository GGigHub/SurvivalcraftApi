using GameEntitySystem;

namespace Game.Network;

public class AdjustableDelayGatePacket : Packet {
    public override byte ID => (byte)PacketType.EditableAdjustableDelayGate;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    private bool m_editAsItem;
    private int m_inventoryId;
    private int m_slotIndex;
    private CellFace m_cellFace;
    private int m_delay;
    public AdjustableDelayGatePacket() { }
    public AdjustableDelayGatePacket(CellFace cell, bool editAsItem, int inventoryId, int slotIndex, int delay) {
        m_cellFace = cell;
        m_editAsItem = editAsItem;
        m_inventoryId = inventoryId;
        m_slotIndex = slotIndex;
        m_delay = delay;
    }
    public override void Serialize(PacketSerializer archive) {
        EditableBlockHelpers.SerializeEditableTarget(archive, ref m_editAsItem, ref m_inventoryId, ref m_slotIndex, ref m_cellFace);
        archive.Value(ref m_delay);
    }
    public override void Handle(bool isServer) {
        if (m_editAsItem) {
            GameManager.Project.FindSubsystem<SubsystemInventories>().FindInventoryById(m_inventoryId, inventory => {
                EditableBlockHelpers.ReplaceDataAtSlot(inventory, m_slotIndex, d => AdjustableDelayGateBlock.SetDelay(d, m_delay));
            });
        }
        else {
            SubsystemTerrain st = GameManager.Project.FindSubsystem<SubsystemTerrain>();
            int value = st.Terrain.GetCellValue(m_cellFace.X, m_cellFace.Y, m_cellFace.Z);
            int newValue = Terrain.ReplaceData(value, AdjustableDelayGateBlock.SetDelay(Terrain.ExtractData(value), m_delay));
            st.ChangeCell(m_cellFace.X, m_cellFace.Y, m_cellFace.Z, newValue);
        }
        if (isServer) NetworkManager.QueuePacket(this);
    }
}

