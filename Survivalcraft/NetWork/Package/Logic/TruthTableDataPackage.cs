using GameEntitySystem;

namespace Game.NetWork;

public class TruthTableDataPackage : IPackage {
    public override byte ID => (byte)PackageType.EditableTruthTable;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    private bool m_syncItem;
    private bool m_editAsItem;
    private int m_slotIndex;
    private int m_inventoryId;
    private int? m_id;
    private CellFace m_cellFace;
    private byte[] m_data;

    public TruthTableDataPackage() { }
    public TruthTableDataPackage(CellFace cell, bool editAsItem, int inventoryId, int slotIndex, TruthTableData data) {
        m_cellFace = cell;
        m_editAsItem = editAsItem;
        m_inventoryId = inventoryId;
        m_slotIndex = slotIndex;
        m_data = data.Data.ToArray();
    }
    public TruthTableDataPackage(int id, TruthTableData data) {
        m_syncItem = true;
        m_slotIndex = id;
        m_data = data.Data.ToArray();
    }
    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_syncItem);
        if (m_syncItem) {
            archive.Value(ref m_slotIndex);
        }
        else {
            EditableBlockPackageHelpers.SerializeEditableTarget(archive, ref m_editAsItem, ref m_inventoryId, ref m_slotIndex, ref m_cellFace);
            EditableBlockPackageHelpers.SerializeOptionalId(archive, ref m_id);
        }
        EditableBlockPackageHelpers.SerializeByteArray(archive, ref m_data);
    }
    public override void Handle(bool isServer) {
        SubsystemTruthTableCircuitBlockBehavior behavior = GameManager.Project.FindSubsystem<SubsystemTruthTableCircuitBlockBehavior>();
        TruthTableData data = new() { Data = m_data };
        if (m_syncItem) {
            behavior.m_itemsData[m_slotIndex] = data;
        }
        else if (m_editAsItem) {
            GameManager.Project.FindSubsystem<SubsystemInventories>().FindInventoryById(m_inventoryId, inventory => {
                if (!m_id.HasValue) {
                    m_id = behavior.StoreItemDataAtUniqueId(data);
                    EditableBlockPackageHelpers.ReplaceDataAtSlot(inventory, m_slotIndex, _ => m_id.Value);
                }
                else {
                    behavior.m_itemsData[m_id.Value] = data;
                }
            });
        }
        else {
            behavior.SetBlockData(m_cellFace.Point, data);
        }
        if (isServer) NetworkManager.QueuePackage(this);
    }
}