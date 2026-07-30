using Engine;
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
    internal byte[] m_data = [];
    public byte m_lastOutput;

    public MemoryBankDataPacket() { }
    public MemoryBankDataPacket(CellFace cell, bool editAsItem, int inventoryId, int slotIndex, MemoryBankData data) {
        m_cellFace = cell;
        m_editAsItem = editAsItem;
        m_inventoryId = inventoryId;
        m_slotIndex = slotIndex;
        m_data = data.Data.ToArray();
        m_lastOutput = data.LastOutput;
        InitializeRequest(
            NetworkLogicElementKind.MemoryBank,
            editAsItem,
            inventoryId,
            slotIndex,
            cell);
    }
    public MemoryBankDataPacket(int id, MemoryBankData data) {
        m_syncItem = true;
        m_slotIndex = id;
        m_data = data.Data.ToArray();
        m_lastOutput = data.LastOutput;
        if (GameManager.Project != null) {
            EditableTarget target = Target(true, 0, id, default);
            long revision = NetworkLogicStateTracker.AdvanceAuthoritativeRevision(
                GameManager.Project,
                target.Key(NetworkLogicElementKind.MemoryBank));
            InitializeState(
                NetworkLogicElementKind.MemoryBank,
                true,
                0,
                id,
                default,
                revision);
        }
    }
    public override void Serialize(PacketSerializer archive) {
        SerializeMutationEnvelope(archive);
        archive.Value(ref m_syncItem);
        if (m_syncItem) {
            archive.Value(ref m_slotIndex);
        }
        else {
            SerializeEditableTarget(archive, ref m_editAsItem, ref m_inventoryId, ref m_slotIndex, ref m_cellFace);
            SerializeOptionalId(archive, ref m_id);
        }
        SerializeByteArray(archive, ref m_data);
        archive.Value(ref m_lastOutput);
    }
    public override void Handle(bool isServer) {
        if (!IsValidData()) {
            return;
        }
        SubsystemMemoryBankBlockBehavior behavior = GameManager.Project.FindSubsystem<SubsystemMemoryBankBlockBehavior>();
        if (m_syncItem) {
            if (isServer
                || m_packetKind != EditableLogicPacketKind.State
                || m_result == EditableLogicResultCode.None
                || m_slotIndex <= 0
                || m_revision < 0) {
                return;
            }
            EditableTarget syncTarget = Target(true, 0, m_slotIndex, default);
            if (NetworkLogicStateTracker.Receive(
                    GameManager.Project,
                    syncTarget.Key(NetworkLogicElementKind.MemoryBank),
                    m_revision).Apply) {
                behavior.m_itemsData[m_slotIndex] = CreateData();
            }
            return;
        }
        EditableTarget target = Target(m_editAsItem, m_inventoryId, m_slotIndex, m_cellFace);
        if (isServer) {
            EditableLogicResultCode result = ValidateServerRequest(
                NetworkLogicElementKind.MemoryBank,
                target,
                MemoryBankBlock.Index,
                out IInventory inventory,
                out int currentValue);
            if (result == EditableLogicResultCode.Accepted) {
                MemoryBankData proposed = CreateData();
                proposed.LastOutput = GetCurrentData(behavior, target, currentValue)?.LastOutput ?? 0;
                m_lastOutput = proposed.LastOutput;
                Apply(behavior, target, inventory, proposed);
                PrepareAuthoritativeState(NetworkLogicElementKind.MemoryBank, target);
                QueueAuthoritativeInventoryState(inventory);
                NetworkManager.Queue(this);
            }
            else if (From != null && GameManager.Project != null) {
                LoadCurrentData(behavior, target, currentValue);
                PrepareAuthoritativeState(
                    NetworkLogicElementKind.MemoryBank,
                    target,
                    result,
                    advanceRevision: false);
                To = From;
                NetworkManager.Queue(this);
            }
            return;
        }
        if (!TryAcceptClientState(
                NetworkLogicElementKind.MemoryBank,
                target,
                MemoryBankBlock.Index,
                out IInventory clientInventory,
                out _)) {
            return;
        }
        Apply(behavior, target, clientInventory, CreateData());
    }

    private MemoryBankData CreateData() {
        MemoryBankData data = new() { LastOutput = m_lastOutput };
        foreach (byte item in m_data) {
            data.Data.Add(item);
        }
        return data;
    }

    private bool IsValidData() =>
        m_data != null
        && m_data.Length <= 256
        && m_lastOutput <= 15
        && m_data.All(value => value <= 15);

    private void Apply(
        SubsystemMemoryBankBlockBehavior behavior,
        EditableTarget target,
        IInventory inventory,
        MemoryBankData data) {
        if (target.IsItem) {
            if (!m_id.HasValue) {
                m_id = behavior.StoreItemDataAtUniqueId(data);
                ReplaceDataAtSlot(inventory, target.SlotIndex, _ => m_id.Value);
            }
            else {
                behavior.m_itemsData[m_id.Value] = data;
                ReplaceDataAtSlot(inventory, target.SlotIndex, _ => m_id.Value);
            }
            return;
        }
        behavior.SetBlockData(target.CellFace.Point, data);
        QueueElectricElement(target.CellFace.Point, MemoryBankBlock.Index);
    }

    private void LoadCurrentData(
        SubsystemMemoryBankBlockBehavior behavior,
        EditableTarget target,
        int currentValue) {
        if (target.IsItem
            && Terrain.ExtractContents(currentValue) == MemoryBankBlock.Index) {
            m_id = Terrain.ExtractData(currentValue);
        }
        MemoryBankData data = GetCurrentData(behavior, target, currentValue);
        m_data = data?.Data.ToArray() ?? [];
        m_lastOutput = data?.LastOutput ?? 0;
    }

    private MemoryBankData GetCurrentData(
        SubsystemMemoryBankBlockBehavior behavior,
        EditableTarget target,
        int currentValue) {
        if (Terrain.ExtractContents(currentValue) != MemoryBankBlock.Index) {
            return null;
        }
        if (!target.IsItem) {
            return behavior.GetBlockData(target.CellFace.Point);
        }
        return behavior.GetItemData(Terrain.ExtractData(currentValue));
    }

    private static void QueueElectricElement(Point3 point, int expectedContents) {
        SubsystemTerrain terrain = GameManager.Project.FindSubsystem<SubsystemTerrain>(true);
        int value = terrain.Terrain.GetCellValue(point.X, point.Y, point.Z);
        if (Terrain.ExtractContents(value) != expectedContents) {
            return;
        }
        int face = ((MemoryBankBlock)BlocksManager.Blocks[expectedContents]).GetFace(value);
        SubsystemElectricity subsystemElectricity = GameManager.Project.FindSubsystem<SubsystemElectricity>(true);
        ElectricElement electricElement = subsystemElectricity.GetElectricElement(
            point.X,
            point.Y,
            point.Z,
            face);
        if (electricElement != null) {
            subsystemElectricity.QueueElectricElementForSimulation(electricElement, subsystemElectricity.CircuitStep + 1);
        }
    }
}

