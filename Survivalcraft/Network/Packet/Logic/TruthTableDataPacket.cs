using Engine;
using GameEntitySystem;

namespace Game.Network;

public class TruthTableDataPacket : EditableBlockPacketBase {
    public override byte ID => (byte)PacketType.EditableTruthTable;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;
    private bool m_syncItem;
    private bool m_editAsItem;
    private int m_slotIndex;
    private int m_inventoryId;
    private int? m_id;
    private CellFace m_cellFace;
    internal byte[] m_data = [];

    public TruthTableDataPacket() { }
    public TruthTableDataPacket(CellFace cell, bool editAsItem, int inventoryId, int slotIndex, TruthTableData data) {
        m_cellFace = cell;
        m_editAsItem = editAsItem;
        m_inventoryId = inventoryId;
        m_slotIndex = slotIndex;
        m_data = data.Data.ToArray();
        InitializeRequest(
            NetworkLogicElementKind.TruthTable,
            editAsItem,
            inventoryId,
            slotIndex,
            cell);
    }
    public TruthTableDataPacket(int id, TruthTableData data) {
        m_syncItem = true;
        m_slotIndex = id;
        m_data = data.Data.ToArray();
        if (GameManager.Project != null) {
            EditableTarget target = Target(true, 0, id, default);
            long revision = NetworkLogicStateTracker.AdvanceAuthoritativeRevision(
                GameManager.Project,
                target.Key(NetworkLogicElementKind.TruthTable));
            InitializeState(
                NetworkLogicElementKind.TruthTable,
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
    }
    public override void Handle(bool isServer) {
        if (!IsValidData()) {
            return;
        }
        SubsystemTruthTableCircuitBlockBehavior behavior = GameManager.Project.FindSubsystem<SubsystemTruthTableCircuitBlockBehavior>();
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
                    syncTarget.Key(NetworkLogicElementKind.TruthTable),
                    m_revision).Apply) {
                behavior.m_itemsData[m_slotIndex] = CreateData();
            }
            return;
        }
        EditableTarget target = Target(m_editAsItem, m_inventoryId, m_slotIndex, m_cellFace);
        if (isServer) {
            EditableLogicResultCode result = ValidateServerRequest(
                NetworkLogicElementKind.TruthTable,
                target,
                TruthTableCircuitBlock.Index,
                out IInventory inventory,
                out int currentValue);
            if (result == EditableLogicResultCode.Accepted) {
                Apply(behavior, target, inventory, CreateData());
                PrepareAuthoritativeState(NetworkLogicElementKind.TruthTable, target);
                QueueAuthoritativeInventoryState(inventory);
                NetworkManager.Queue(this);
            }
            else if (From != null && GameManager.Project != null) {
                LoadCurrentData(behavior, target, currentValue);
                PrepareAuthoritativeState(
                    NetworkLogicElementKind.TruthTable,
                    target,
                    result,
                    advanceRevision: false);
                To = From;
                NetworkManager.Queue(this);
            }
            return;
        }
        if (!TryAcceptClientState(
                NetworkLogicElementKind.TruthTable,
                target,
                TruthTableCircuitBlock.Index,
                out IInventory clientInventory,
                out _)) {
            return;
        }
        Apply(behavior, target, clientInventory, CreateData());
    }

    private TruthTableData CreateData() =>
        new() { Data = (byte[])m_data.Clone() };

    private bool IsValidData() =>
        m_data is { Length: 16 }
        && m_data.All(value => value <= 15);

    private void Apply(
        SubsystemTruthTableCircuitBlockBehavior behavior,
        EditableTarget target,
        IInventory inventory,
        TruthTableData data) {
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
        QueueElectricElement(target.CellFace.Point);
    }

    private void LoadCurrentData(
        SubsystemTruthTableCircuitBlockBehavior behavior,
        EditableTarget target,
        int currentValue) {
        TruthTableData data = null;
        if (Terrain.ExtractContents(currentValue) == TruthTableCircuitBlock.Index) {
            if (target.IsItem) {
                m_id = Terrain.ExtractData(currentValue);
                data = behavior.GetItemData(m_id.Value);
            }
            else {
                data = behavior.GetBlockData(target.CellFace.Point);
            }
        }
        m_data = data?.Data.ToArray() ?? new byte[16];
    }

    private static void QueueElectricElement(Point3 point) {
        SubsystemTerrain terrain = GameManager.Project.FindSubsystem<SubsystemTerrain>(true);
        int value = terrain.Terrain.GetCellValue(point.X, point.Y, point.Z);
        if (Terrain.ExtractContents(value) != TruthTableCircuitBlock.Index) {
            return;
        }
        int face = ((TruthTableCircuitBlock)BlocksManager.Blocks[TruthTableCircuitBlock.Index])
            .GetFace(value);
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

