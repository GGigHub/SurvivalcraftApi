using GameEntitySystem;

namespace Game.Network;

public class BatteryVoltagePacket : EditableBlockPacketBase {
    public override byte ID => (byte)PacketType.EditableBattery;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;
    private bool m_editAsItem;
    private int m_inventoryId;
    private int m_slotIndex;
    private CellFace m_cellFace;
    private int m_voltage;
    public BatteryVoltagePacket() { }
    public BatteryVoltagePacket(CellFace cell, bool editAsItem, int inventoryId, int slotIndex, int voltage) {
        m_cellFace = cell;
        m_editAsItem = editAsItem;
        m_inventoryId = inventoryId;
        m_slotIndex = slotIndex;
        m_voltage = voltage;
        InitializeRequest(
            NetworkLogicElementKind.Battery,
            editAsItem,
            inventoryId,
            slotIndex,
            cell);
    }
    public override void Serialize(PacketSerializer archive) {
        SerializeMutationEnvelope(archive);
        SerializeEditableTarget(archive, ref m_editAsItem, ref m_inventoryId, ref m_slotIndex, ref m_cellFace);
        archive.Value(ref m_voltage);
    }
    public override void Handle(bool isServer) {
        EditableTarget target = Target(m_editAsItem, m_inventoryId, m_slotIndex, m_cellFace);
        if (isServer) {
            EditableLogicResultCode result = ValidateServerRequest(
                NetworkLogicElementKind.Battery,
                target,
                BatteryBlock.Index,
                out IInventory inventory,
                out int currentValue);
            if (result == EditableLogicResultCode.Accepted
                && m_voltage is < 0 or > 15) {
                result = EditableLogicResultCode.InvalidValue;
            }
            if (result == EditableLogicResultCode.Accepted) {
                Apply(target, inventory, currentValue);
                PrepareAuthoritativeState(NetworkLogicElementKind.Battery, target);
                QueueAuthoritativeInventoryState(inventory);
                NetworkManager.Queue(this);
            }
            else if (From != null && GameManager.Project != null) {
                if (Terrain.ExtractContents(currentValue) == BatteryBlock.Index) {
                    m_voltage = BatteryBlock.GetVoltageLevel(Terrain.ExtractData(currentValue));
                }
                PrepareAuthoritativeState(
                    NetworkLogicElementKind.Battery,
                    target,
                    result,
                    advanceRevision: false);
                To = From;
                NetworkManager.Queue(this);
            }
            return;
        }
        if (m_voltage is < 0 or > 15
            || !TryAcceptClientState(
                NetworkLogicElementKind.Battery,
                target,
                BatteryBlock.Index,
                out IInventory clientInventory,
                out int clientValue)) {
            return;
        }
        Apply(target, clientInventory, clientValue);
    }

    private void Apply(EditableTarget target, IInventory inventory, int currentValue) {
        if (target.IsItem) {
            ReplaceDataAtSlot(
                inventory,
                target.SlotIndex,
                data => BatteryBlock.SetVoltageLevel(data, m_voltage));
            return;
        }
        SubsystemTerrain terrain = GameManager.Project.FindSubsystem<SubsystemTerrain>();
        int newValue = Terrain.ReplaceData(
            currentValue,
            BatteryBlock.SetVoltageLevel(Terrain.ExtractData(currentValue), m_voltage));
        terrain.ChangeCell(
            target.CellFace.X,
            target.CellFace.Y,
            target.CellFace.Z,
            newValue);
    }
}

