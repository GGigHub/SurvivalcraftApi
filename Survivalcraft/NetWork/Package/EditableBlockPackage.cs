using GameEntitySystem;

namespace Game.NetWork.Packages;

internal static class EditableBlockPackageHelpers {
    public static void SerializeEditableTarget(PackageSerializer archive, ref bool editAsItem, ref int inventoryId, ref int slotIndex, ref CellFace cellFace) {
        archive.Value(ref editAsItem);
        if (editAsItem) {
            archive.Value(ref inventoryId);
            archive.Value(ref slotIndex);
        }
        else {
            archive.Value(ref cellFace);
        }
    }

    public static void SerializeOptionalId(PackageSerializer archive, ref int? id) {
        bool hasId = id.HasValue;
        archive.Value(ref hasId);
        if (hasId) {
            int value = id ?? 0;
            archive.Value(ref value);
            if (archive.IsReading) {
                id = value;
            }
        }
        else if (archive.IsReading) {
            id = null;
        }
    }

    public static void SerializeByteArray(PackageSerializer archive, ref byte[] data) {
        ushort count = archive.IsWriting ? (ushort)data.Length : (ushort)0;
        archive.Value(ref count);
        byte[] buffer = archive.IsWriting ? data : null;
        archive.Raw(ref buffer, count);
        if (archive.IsReading) {
            data = buffer;
        }
    }

    public static int ReplaceDataAtSlot(IInventory inventory, int slotIndex, Func<int, int> newData) {
        int value = inventory.GetSlotValue(slotIndex);
        int count = inventory.GetSlotCount(slotIndex);
        inventory.RemoveSlotItems(slotIndex, count);
        int newValue = Terrain.ReplaceData(value, newData(Terrain.ExtractData(value)));
        inventory.AddSlotItems(slotIndex, newValue, count);
        return newValue;
    }
}

public class MemoryBankDataPackage : IPackage {
    public override byte ID => (byte)PackageType.EditableMemoryBank;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    private bool m_syncItem;
    private bool m_editAsItem;
    private int m_slotIndex;
    private int m_inventoryId;
    private int? m_id;
    private CellFace m_cellFace;
    private byte[] m_data;

    public MemoryBankDataPackage() { }
    public MemoryBankDataPackage(CellFace cell, bool editAsItem, int inventoryId, int slotIndex, MemoryBankData data) {
        m_cellFace = cell;
        m_editAsItem = editAsItem;
        m_inventoryId = inventoryId;
        m_slotIndex = slotIndex;
        m_data = data.Data.ToArray();
    }
    public MemoryBankDataPackage(int id, MemoryBankData data) {
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

public class AdjustableDelayGatePackage : IPackage {
    public override byte ID => (byte)PackageType.EditableAdjustableDelayGate;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    private bool m_editAsItem;
    private int m_inventoryId;
    private int m_slotIndex;
    private CellFace m_cellFace;
    private int m_delay;
    public AdjustableDelayGatePackage() { }
    public AdjustableDelayGatePackage(CellFace cell, bool editAsItem, int inventoryId, int slotIndex, int delay) {
        m_cellFace = cell;
        m_editAsItem = editAsItem;
        m_inventoryId = inventoryId;
        m_slotIndex = slotIndex;
        m_delay = delay;
    }
    public override void Serialize(PackageSerializer archive) {
        EditableBlockPackageHelpers.SerializeEditableTarget(archive, ref m_editAsItem, ref m_inventoryId, ref m_slotIndex, ref m_cellFace);
        archive.Value(ref m_delay);
    }
    public override void Handle(bool isServer) {
        if (m_editAsItem) {
            GameManager.Project.FindSubsystem<SubsystemInventories>().FindInventoryById(m_inventoryId, inventory => {
                EditableBlockPackageHelpers.ReplaceDataAtSlot(inventory, m_slotIndex, d => AdjustableDelayGateBlock.SetDelay(d, m_delay));
            });
        }
        else {
            SubsystemTerrain st = GameManager.Project.FindSubsystem<SubsystemTerrain>();
            int value = st.Terrain.GetCellValue(m_cellFace.X, m_cellFace.Y, m_cellFace.Z);
            int newValue = Terrain.ReplaceData(value, AdjustableDelayGateBlock.SetDelay(Terrain.ExtractData(value), m_delay));
            st.ChangeCell(m_cellFace.X, m_cellFace.Y, m_cellFace.Z, newValue);
        }
        if (isServer) NetworkManager.QueuePackage(this);
    }
}

public class BatteryVoltagePackage : IPackage {
    public override byte ID => (byte)PackageType.EditableBattery;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    private bool m_editAsItem;
    private int m_inventoryId;
    private int m_slotIndex;
    private CellFace m_cellFace;
    private int m_voltage;
    public BatteryVoltagePackage() { }
    public BatteryVoltagePackage(CellFace cell, bool editAsItem, int inventoryId, int slotIndex, int voltage) {
        m_cellFace = cell;
        m_editAsItem = editAsItem;
        m_inventoryId = inventoryId;
        m_slotIndex = slotIndex;
        m_voltage = voltage;
    }
    public override void Serialize(PackageSerializer archive) {
        EditableBlockPackageHelpers.SerializeEditableTarget(archive, ref m_editAsItem, ref m_inventoryId, ref m_slotIndex, ref m_cellFace);
        archive.Value(ref m_voltage);
    }
    public override void Handle(bool isServer) {
        if (m_editAsItem) {
            GameManager.Project.FindSubsystem<SubsystemInventories>().FindInventoryById(m_inventoryId, inventory => {
                EditableBlockPackageHelpers.ReplaceDataAtSlot(inventory, m_slotIndex, d => BatteryBlock.SetVoltageLevel(d, m_voltage));
            });
        }
        else {
            SubsystemTerrain st = GameManager.Project.FindSubsystem<SubsystemTerrain>();
            int value = st.Terrain.GetCellValue(m_cellFace.X, m_cellFace.Y, m_cellFace.Z);
            int newValue = Terrain.ReplaceData(value, BatteryBlock.SetVoltageLevel(Terrain.ExtractData(value), m_voltage));
            st.ChangeCell(m_cellFace.X, m_cellFace.Y, m_cellFace.Z, newValue);
        }
        if (isServer) NetworkManager.QueuePackage(this);
    }
}

public class SwitchVoltagePackage : IPackage {
    public override byte ID => (byte)PackageType.EditableSwitch;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    private bool m_editAsItem;
    private int m_inventoryId;
    private int m_slotIndex;
    private CellFace m_cellFace;
    private int m_voltage;
    public SwitchVoltagePackage() { }
    public SwitchVoltagePackage(CellFace cell, bool editAsItem, int inventoryId, int slotIndex, int voltage) {
        m_cellFace = cell;
        m_editAsItem = editAsItem;
        m_inventoryId = inventoryId;
        m_slotIndex = slotIndex;
        m_voltage = voltage;
    }
    public override void Serialize(PackageSerializer archive) {
        EditableBlockPackageHelpers.SerializeEditableTarget(archive, ref m_editAsItem, ref m_inventoryId, ref m_slotIndex, ref m_cellFace);
        archive.Value(ref m_voltage);
    }
    public override void Handle(bool isServer) {
        if (m_editAsItem) {
            GameManager.Project.FindSubsystem<SubsystemInventories>().FindInventoryById(m_inventoryId, inventory => {
                EditableBlockPackageHelpers.ReplaceDataAtSlot(inventory, m_slotIndex, d => SwitchBlock.SetVoltageLevel(d, m_voltage));
            });
        }
        else {
            SubsystemTerrain st = GameManager.Project.FindSubsystem<SubsystemTerrain>();
            int value = st.Terrain.GetCellValue(m_cellFace.X, m_cellFace.Y, m_cellFace.Z);
            int newValue = Terrain.ReplaceData(value, SwitchBlock.SetVoltageLevel(Terrain.ExtractData(value), m_voltage));
            st.ChangeCell(m_cellFace.X, m_cellFace.Y, m_cellFace.Z, newValue);
        }
        if (isServer) NetworkManager.QueuePackage(this);
    }
}

public class ButtonVoltagePackage : IPackage {
    public override byte ID => (byte)PackageType.EditableButton;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    private bool m_editAsItem;
    private int m_inventoryId;
    private int m_slotIndex;
    private CellFace m_cellFace;
    private int m_voltage;
    public ButtonVoltagePackage() { }
    public ButtonVoltagePackage(CellFace cell, bool editAsItem, int inventoryId, int slotIndex, int voltage) {
        m_cellFace = cell;
        m_editAsItem = editAsItem;
        m_inventoryId = inventoryId;
        m_slotIndex = slotIndex;
        m_voltage = voltage;
    }
    public override void Serialize(PackageSerializer archive) {
        EditableBlockPackageHelpers.SerializeEditableTarget(archive, ref m_editAsItem, ref m_inventoryId, ref m_slotIndex, ref m_cellFace);
        archive.Value(ref m_voltage);
    }
    public override void Handle(bool isServer) {
        if (m_editAsItem) {
            GameManager.Project.FindSubsystem<SubsystemInventories>().FindInventoryById(m_inventoryId, inventory => {
                EditableBlockPackageHelpers.ReplaceDataAtSlot(inventory, m_slotIndex, d => ButtonBlock.SetVoltageLevel(d, m_voltage));
            });
        }
        else {
            SubsystemTerrain st = GameManager.Project.FindSubsystem<SubsystemTerrain>();
            int value = st.Terrain.GetCellValue(m_cellFace.X, m_cellFace.Y, m_cellFace.Z);
            int newValue = Terrain.ReplaceData(value, ButtonBlock.SetVoltageLevel(Terrain.ExtractData(value), m_voltage));
            st.ChangeCell(m_cellFace.X, m_cellFace.Y, m_cellFace.Z, newValue);
        }
        if (isServer) NetworkManager.QueuePackage(this);
    }
}

public class PistonDataPackage : IPackage {
    public override byte ID => (byte)PackageType.EditablePiston;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    private bool m_editAsItem;
    private int m_inventoryId;
    private int m_slotIndex;
    private CellFace m_cellFace;
    private int m_data;
    public PistonDataPackage() { }
    public PistonDataPackage(CellFace cell, bool editAsItem, int inventoryId, int slotIndex, int data) {
        m_cellFace = cell;
        m_editAsItem = editAsItem;
        m_inventoryId = inventoryId;
        m_slotIndex = slotIndex;
        m_data = data;
    }
    public override void Serialize(PackageSerializer archive) {
        EditableBlockPackageHelpers.SerializeEditableTarget(archive, ref m_editAsItem, ref m_inventoryId, ref m_slotIndex, ref m_cellFace);
        archive.Value(ref m_data);
    }
    public override void Handle(bool isServer) {
        if (m_editAsItem) {
            GameManager.Project.FindSubsystem<SubsystemInventories>().FindInventoryById(m_inventoryId, inventory => {
                EditableBlockPackageHelpers.ReplaceDataAtSlot(inventory, m_slotIndex, _ => m_data);
            });
        }
        else {
            SubsystemTerrain st = GameManager.Project.FindSubsystem<SubsystemTerrain>();
            int value = st.Terrain.GetCellValue(m_cellFace.X, m_cellFace.Y, m_cellFace.Z);
            int newValue = Terrain.ReplaceData(value, m_data);
            st.ChangeCell(m_cellFace.X, m_cellFace.Y, m_cellFace.Z, newValue);
        }
        if (isServer) NetworkManager.QueuePackage(this);
    }
}
