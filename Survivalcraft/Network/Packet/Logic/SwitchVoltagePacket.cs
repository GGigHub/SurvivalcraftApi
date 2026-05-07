using GameEntitySystem;

namespace Game.Network;

public class SwitchVoltagePacket : Packet {
    public override byte ID => (byte)PacketType.EditableSwitch;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    private bool m_editAsItem;
    private int m_inventoryId;
    private int m_slotIndex;
    private CellFace m_cellFace;
    private int m_voltage;
    public SwitchVoltagePacket() { }
    public SwitchVoltagePacket(CellFace cell, bool editAsItem, int inventoryId, int slotIndex, int voltage) {
        m_cellFace = cell;
        m_editAsItem = editAsItem;
        m_inventoryId = inventoryId;
        m_slotIndex = slotIndex;
        m_voltage = voltage;
    }
    public override void Serialize(PacketSerializer archive) {
        EditableBlockHelpers.SerializeEditableTarget(archive, ref m_editAsItem, ref m_inventoryId, ref m_slotIndex, ref m_cellFace);
        archive.Value(ref m_voltage);
    }
    public override void Handle(bool isServer) {
        if (m_editAsItem) {
            GameManager.Project.FindSubsystem<SubsystemInventories>().FindInventoryById(m_inventoryId, inventory => {
                EditableBlockHelpers.ReplaceDataAtSlot(inventory, m_slotIndex, d => SwitchBlock.SetVoltageLevel(d, m_voltage));
            });
        }
        else {
            SubsystemTerrain st = GameManager.Project.FindSubsystem<SubsystemTerrain>();
            int value = st.Terrain.GetCellValue(m_cellFace.X, m_cellFace.Y, m_cellFace.Z);
            int newValue = Terrain.ReplaceData(value, SwitchBlock.SetVoltageLevel(Terrain.ExtractData(value), m_voltage));
            st.ChangeCell(m_cellFace.X, m_cellFace.Y, m_cellFace.Z, newValue);
        }
        if (isServer) NetworkManager.QueuePacket(this);
    }
}

