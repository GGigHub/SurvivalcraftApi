using GameEntitySystem;

namespace Game.NetWork.Packages;

public enum EditableType : byte {
    MemoryBank = 0,
    TruthTable = 1,
    AdjustableDelayGate = 2,
    Battery = 3,
    Piston = 4,
    Switch = 5,
    Button = 6
}

/// <summary>
/// 鍩虹鍖呮ā鏉垮鍒?
/// </summary>
public class EditableBlockPackage : IPackage {
    public override byte ID => (byte)PackageType.EditableBlock;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;

    private EditableType m_itemType;
    private bool m_editAsItem;
    private int m_slotIndex;
    private int m_inventoryId;
    private int m_delay;
    private int? m_id;
    private CellFace m_cellFace;
    private byte[] m_Data;
    private bool syncItem;

    public EditableBlockPackage() {
    }

    public EditableBlockPackage(EditableType itemType, CellFace cell, bool eidtAsItem, int indentoryId, int slotIndex, int delay) {
        m_itemType = itemType;
        m_inventoryId = indentoryId;
        m_cellFace = cell;
        m_editAsItem = eidtAsItem;
        m_slotIndex = slotIndex;
        m_delay = delay;
    }

    public EditableBlockPackage(CellFace cell, bool eidtAsItem, int indentoryId, int slotIndex, TruthTableData truthTableData) {
        m_itemType = EditableType.TruthTable;
        m_cellFace = cell;
        m_editAsItem = eidtAsItem;
        m_inventoryId = indentoryId;
        m_slotIndex = slotIndex;
        m_Data = truthTableData.Data.ToArray();
    }

    public EditableBlockPackage(CellFace cell, bool eidtAsItem, int indentoryId, int slotIndex, MemoryBankData memoryBankData) {
        m_itemType = EditableType.MemoryBank;
        m_cellFace = cell;
        m_editAsItem = eidtAsItem;
        m_inventoryId = indentoryId;
        m_slotIndex = slotIndex;
        m_Data = memoryBankData.Data.ToArray();
    }

    public EditableBlockPackage(int id, TruthTableData truthTableData) {
        m_itemType = EditableType.TruthTable;
        m_Data = truthTableData.Data.ToArray();
        m_slotIndex = id;
        syncItem = true;
    }

    public EditableBlockPackage(int id, MemoryBankData memoryBankData) {
        m_itemType = EditableType.MemoryBank;
        m_Data = memoryBankData.Data.ToArray();
        syncItem = true;
        m_slotIndex = id;
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Enum(ref m_itemType);
        archive.Value(ref syncItem);
        if (syncItem) {
            archive.Value(ref m_slotIndex);
        }
        else {
            archive.Value(ref m_editAsItem);
            if (m_editAsItem) {
                archive.Value(ref m_inventoryId);
                archive.Value(ref m_slotIndex);
            }
            else {
                archive.Value(ref m_cellFace);
            }
        }
        switch (m_itemType) {
            case EditableType.TruthTable:
            case EditableType.MemoryBank:
                bool hasId = m_id.HasValue;
                archive.Value(ref hasId);
                if (hasId) {
                    int id = m_id ?? 0;
                    archive.Value(ref id);
                    if (archive.IsReading) m_id = id;
                }
                else if (archive.IsReading) {
                    m_id = null;
                }
                ushort count = archive.IsWriting ? (ushort)m_Data.Length : (ushort)0;
                archive.Value(ref count);
                byte[] data = archive.IsWriting ? m_Data : null;
                archive.Raw(ref data, count);
                if (archive.IsReading) {
                    m_Data = data;
                }
                break;
            default:
                archive.Value(ref m_delay);
                break;
        }
    }

    public override void Handle(bool isServer) {
        var subInventory = GameManager.Project.FindSubsystem<SubsystemInventories>();
        switch (m_itemType) {
            case EditableType.MemoryBank:
                var behavior = GameManager.Project.FindSubsystem<SubsystemMemoryBankBlockBehavior>();
                var data = new MemoryBankData();
                foreach (var item in m_Data) data.Data.Add(item);
                if (syncItem) {
                    behavior.m_itemsData[m_slotIndex] = data;
                }
                else if (m_editAsItem) {
                    subInventory.FindInventoryById(m_inventoryId, inventory => {
                        if (!m_id.HasValue) {
                            m_id = behavior.StoreItemDataAtUniqueId(data);
                            ReplaceDataAtSlot(inventory, m_slotIndex, d => m_id.Value);
                        }
                        else {
                            behavior.m_itemsData[m_id.Value] = data;
                        }
                    });
                }
                else {
                    behavior.SetBlockData(m_cellFace.Point, data);
                }
                break;
            case EditableType.TruthTable:
                var truthBehavior = GameManager.Project.FindSubsystem<SubsystemTruthTableCircuitBlockBehavior>();
                var truthData = new TruthTableData();
                truthData.Data = m_Data;
                if (syncItem) {
                    truthBehavior.m_itemsData[m_slotIndex] = truthData;
                }
                else if (m_editAsItem) {
                    subInventory.FindInventoryById(m_inventoryId, inventory => {
                        if (m_id.HasValue) {
                            m_id = truthBehavior.StoreItemDataAtUniqueId(truthData);
                            ReplaceDataAtSlot(inventory, m_slotIndex, d => m_id.Value);
                        }
                        else {
                            truthBehavior.m_itemsData[m_id.Value] = truthData;
                        }
                    });
                }
                else {
                    truthBehavior.SetBlockData(m_cellFace.Point, truthData);
                }
                break;
            case EditableType.AdjustableDelayGate:
                if (m_editAsItem) {
                    subInventory.FindInventoryById(m_inventoryId, inventory => {
                        ReplaceDataAtSlot(inventory, m_slotIndex, d => AdjustableDelayGateBlock.SetDelay(d, m_delay));
                    });
                }
                else {
                    SubsystemTerrain st = GameManager.Project.FindSubsystem<SubsystemTerrain>();
                    int value = st.Terrain.GetCellValue(m_cellFace.X, m_cellFace.Y, m_cellFace.Z);
                    int newValue = Terrain.ReplaceData(value, AdjustableDelayGateBlock.SetDelay(Terrain.ExtractData(value), m_delay));
                    st.ChangeCell(m_cellFace.X, m_cellFace.Y, m_cellFace.Z, newValue);
                }
                break;
            case EditableType.Battery:
                if (m_editAsItem) {
                    subInventory.FindInventoryById(m_inventoryId, inventory => {
                        ReplaceDataAtSlot(inventory, m_slotIndex, d => BatteryBlock.SetVoltageLevel(d, m_delay));
                    });
                }
                else {
                    SubsystemTerrain st = GameManager.Project.FindSubsystem<SubsystemTerrain>();
                    int value = st.Terrain.GetCellValue(m_cellFace.X, m_cellFace.Y, m_cellFace.Z);
                    int newValue = Terrain.ReplaceData(value, BatteryBlock.SetVoltageLevel(Terrain.ExtractData(value), m_delay));
                    st.ChangeCell(m_cellFace.X, m_cellFace.Y, m_cellFace.Z, newValue);
                }
                break;
            case EditableType.Switch:
                if (m_editAsItem) {
                    subInventory.FindInventoryById(m_inventoryId, inventory => {
                        ReplaceDataAtSlot(inventory, m_slotIndex, d => SwitchBlock.SetVoltageLevel(d, m_delay));
                    });
                }
                else {
                    SubsystemTerrain st = GameManager.Project.FindSubsystem<SubsystemTerrain>();
                    int value = st.Terrain.GetCellValue(m_cellFace.X, m_cellFace.Y, m_cellFace.Z);
                    int newValue = Terrain.ReplaceData(value, SwitchBlock.SetVoltageLevel(Terrain.ExtractData(value), m_delay));
                    st.ChangeCell(m_cellFace.X, m_cellFace.Y, m_cellFace.Z, newValue);
                }
                break;
            case EditableType.Button:
                if (m_editAsItem) {
                    subInventory.FindInventoryById(m_inventoryId, inventory => {
                        ReplaceDataAtSlot(inventory, m_slotIndex, d => ButtonBlock.SetVoltageLevel(d, m_delay));
                    });
                }
                else {
                    SubsystemTerrain st = GameManager.Project.FindSubsystem<SubsystemTerrain>();
                    int value = st.Terrain.GetCellValue(m_cellFace.X, m_cellFace.Y, m_cellFace.Z);
                    int newValue = Terrain.ReplaceData(value, ButtonBlock.SetVoltageLevel(Terrain.ExtractData(value), m_delay));
                    st.ChangeCell(m_cellFace.X, m_cellFace.Y, m_cellFace.Z, newValue);
                }
                break;
            case EditableType.Piston:
                if (m_editAsItem) {
                    subInventory.FindInventoryById(m_inventoryId, inventory => {
                        ReplaceDataAtSlot(inventory, m_slotIndex, d => m_delay);
                    });
                }
                else {
                    SubsystemTerrain st = GameManager.Project.FindSubsystem<SubsystemTerrain>();
                    int value = st.Terrain.GetCellValue(m_cellFace.X, m_cellFace.Y, m_cellFace.Z);
                    int newValue = Terrain.ReplaceData(value, m_delay);
                    st.ChangeCell(m_cellFace.X, m_cellFace.Y, m_cellFace.Z, newValue);
                }
                break;
        }
        if (isServer) {
            NetworkManager.QueuePackage(this);
        }
    }

    public int ReplaceDataAtSlot(IInventory inventory, int slotIndex, Func<int, int> newData) {
        int value = inventory.GetSlotValue(slotIndex);
        int count = inventory.GetSlotCount(slotIndex);
        inventory.RemoveSlotItems(slotIndex, count);
        int newValue = Terrain.ReplaceData(value, newData(Terrain.ExtractData(value)));
        inventory.AddSlotItems(slotIndex, newValue, count);
        return newValue;
    }
}
