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
/// 基础包模板复制
/// </summary>
public class EditableBlockPackage : IPackage {
    public byte ID => (byte)PackageType.EditableBlock;
    public Client To { get; set; }
    public Client Except { get; set; }
    public Client From { get; set; }

    public ClientStage MinNeedState => ClientStage.ProjectLoaded;

    private EditableType m_itemType;
    private bool m_editAsItem;
    private int m_slotIndex;
    private int m_inventoryId;
    private int m_delay;
    private int? m_id;
    private CellFace m_cellFace;
    private byte[] m_Data;
    private bool syncItem;

    public EditableBlockPackage() { }

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
        m_Data = truthTableData.Data.ToArray(); ;
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

    public void Handle(bool isServer) {
        var subInventory = GameManager.Project.FindSubsystem<SubsystemInventories>();
        switch (m_itemType) {
            case EditableType.MemoryBank:
                var behavior = GameManager.Project.FindSubsystem<SubsystemMemoryBankBlockBehavior>();
                var data = new MemoryBankData();
                foreach (var item in m_Data) {
                    data.Data.Add(item);
                }
                if (syncItem) {
                    behavior.m_itemsData[m_slotIndex] = data;
                }
                else {
                    if (m_editAsItem) {
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
                }
                break;
            case EditableType.TruthTable:
                var truthBehavior = GameManager.Project.FindSubsystem<SubsystemTruthTableCircuitBlockBehavior>();
                var truthData = new TruthTableData();
                truthData.Data = m_Data;
                if (syncItem) {
                    truthBehavior.m_itemsData[m_slotIndex] = truthData;
                }
                else {
                    if (m_editAsItem) {
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
    public void ReadData(PackageStreamReader reader) {
        m_itemType = reader.ReadEnum<EditableType>();
        syncItem = reader.ReadBoolean();
        if (syncItem) {
            m_slotIndex = reader.ReadInt32();
        }
        else {
            m_editAsItem = reader.ReadBoolean();
            if (m_editAsItem) {
                m_inventoryId = reader.ReadInt32();
                m_slotIndex = reader.ReadInt32();
            }
            else {
                m_cellFace = reader.ReadCellFace();
            }
        }
        switch (m_itemType) {
            case EditableType.TruthTable:
            case EditableType.MemoryBank:
                if (reader.ReadBoolean()) m_id = reader.ReadInt32();
                ushort count = reader.ReadUInt16();
                m_Data = new byte[count];
                m_Data = reader.ReadBytes(count);
                break;

            default:
                m_delay = reader.ReadInt32();
                break;
        }
    }

    public void WriteData(PackageStreamWriter writer) {
        writer.WriteEnum(m_itemType);
        writer.Write(syncItem);
        if (syncItem) {
            writer.Write(m_slotIndex);
        }
        else {
            writer.Write(m_editAsItem);
            if (m_editAsItem) {
                writer.Write(m_inventoryId);
                writer.Write(m_slotIndex);
            }
            else {
                writer.Write(m_cellFace);
            }
        }
        switch (m_itemType) {
            case EditableType.TruthTable:
            case EditableType.MemoryBank:
                writer.Write(m_id.HasValue);
                if (m_id.HasValue) writer.Write(m_id.Value);
                writer.Write((ushort)m_Data.Length);
                writer.Write(m_Data);
                break;
            default:
                writer.Write(m_delay);
                break;
        }
    }

}

