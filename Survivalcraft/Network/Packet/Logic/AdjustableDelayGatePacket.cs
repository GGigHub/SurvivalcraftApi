using GameEntitySystem;

namespace Game.Network;

public class AdjustableDelayGatePacket : EditableBlockPacketBase {
    public override byte ID => (byte)PacketType.EditableAdjustableDelayGate;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;
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
        InitializeRequest(
            NetworkLogicElementKind.AdjustableDelayGate,
            editAsItem,
            inventoryId,
            slotIndex,
            cell);
    }
    public override void Serialize(PacketSerializer archive) {
        SerializeMutationEnvelope(archive);
        SerializeEditableTarget(archive, ref m_editAsItem, ref m_inventoryId, ref m_slotIndex, ref m_cellFace);
        archive.Value(ref m_delay);
    }
    public override void Handle(bool isServer) {
        EditableTarget target = Target(m_editAsItem, m_inventoryId, m_slotIndex, m_cellFace);
        if (isServer) {
            EditableLogicResultCode result = ValidateServerRequest(
                NetworkLogicElementKind.AdjustableDelayGate,
                target,
                AdjustableDelayGateBlock.Index,
                out IInventory inventory,
                out int currentValue);
            if (result == EditableLogicResultCode.Accepted
                && m_delay is < 0 or > 255) {
                result = EditableLogicResultCode.InvalidValue;
            }
            if (result == EditableLogicResultCode.Accepted) {
                Apply(target, inventory, currentValue);
                PrepareAuthoritativeState(NetworkLogicElementKind.AdjustableDelayGate, target);
                QueueAuthoritativeInventoryState(inventory);
                NetworkManager.Queue(this);
            }
            else if (From != null && GameManager.Project != null) {
                if (Terrain.ExtractContents(currentValue) == AdjustableDelayGateBlock.Index) {
                    m_delay = AdjustableDelayGateBlock.GetDelay(Terrain.ExtractData(currentValue));
                }
                PrepareAuthoritativeState(
                    NetworkLogicElementKind.AdjustableDelayGate,
                    target,
                    result,
                    advanceRevision: false);
                To = From;
                NetworkManager.Queue(this);
            }
            return;
        }
        if (m_delay is < 0 or > 255
            || !TryAcceptClientState(
                NetworkLogicElementKind.AdjustableDelayGate,
                target,
                AdjustableDelayGateBlock.Index,
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
                data => AdjustableDelayGateBlock.SetDelay(data, m_delay));
            return;
        }
        SubsystemTerrain terrain = GameManager.Project.FindSubsystem<SubsystemTerrain>();
        int newValue = Terrain.ReplaceData(
            currentValue,
            AdjustableDelayGateBlock.SetDelay(Terrain.ExtractData(currentValue), m_delay));
        terrain.ChangeCell(
            target.CellFace.X,
            target.CellFace.Y,
            target.CellFace.Z,
            newValue);
    }
}

