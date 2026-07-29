using GameEntitySystem;

namespace Game.Network;

public class PistonDataPacket : EditableBlockPacketBase {
    public override byte ID => (byte)PacketType.EditablePiston;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;
    private bool m_editAsItem;
    private int m_inventoryId;
    private int m_slotIndex;
    private CellFace m_cellFace;
    private int m_data;
    public PistonDataPacket() { }
    public PistonDataPacket(CellFace cell, bool editAsItem, int inventoryId, int slotIndex, int data) {
        m_cellFace = cell;
        m_editAsItem = editAsItem;
        m_inventoryId = inventoryId;
        m_slotIndex = slotIndex;
        m_data = data;
        InitializeRequest(
            NetworkLogicElementKind.Piston,
            editAsItem,
            inventoryId,
            slotIndex,
            cell);
    }
    public override void Serialize(PacketSerializer archive) {
        SerializeMutationEnvelope(archive);
        SerializeEditableTarget(archive, ref m_editAsItem, ref m_inventoryId, ref m_slotIndex, ref m_cellFace);
        archive.Value(ref m_data);
    }
    public override void Handle(bool isServer) {
        EditableTarget target = Target(m_editAsItem, m_inventoryId, m_slotIndex, m_cellFace);
        if (isServer) {
            EditableLogicResultCode result = ValidateServerRequest(
                NetworkLogicElementKind.Piston,
                target,
                PistonBlock.Index,
                out IInventory inventory,
                out int currentValue);
            int currentData = Terrain.ExtractData(currentValue);
            if (result == EditableLogicResultCode.Accepted
                && !IsValidPistonEdit(currentData, m_data)) {
                result = EditableLogicResultCode.InvalidValue;
            }
            if (result == EditableLogicResultCode.Accepted) {
                Apply(target, inventory, currentValue);
                PrepareAuthoritativeState(NetworkLogicElementKind.Piston, target);
                QueueAuthoritativeInventoryState(inventory);
                NetworkManager.Queue(this);
            }
            else if (From != null && GameManager.Project != null) {
                if (Terrain.ExtractContents(currentValue) == PistonBlock.Index) {
                    m_data = currentData;
                }
                PrepareAuthoritativeState(
                    NetworkLogicElementKind.Piston,
                    target,
                    result,
                    advanceRevision: false);
                To = From;
                NetworkManager.Queue(this);
            }
            return;
        }
        if (!IsValidAuthoritativeState(m_data)
            || !TryAcceptClientState(
                NetworkLogicElementKind.Piston,
                target,
                PistonBlock.Index,
                out IInventory clientInventory,
                out int clientValue)) {
            return;
        }
        Apply(target, clientInventory, clientValue);
    }

    private void Apply(EditableTarget target, IInventory inventory, int currentValue) {
        if (target.IsItem) {
            ReplaceDataAtSlot(inventory, target.SlotIndex, _ => m_data);
            return;
        }
        SubsystemTerrain terrain = GameManager.Project.FindSubsystem<SubsystemTerrain>();
        terrain.ChangeCell(
            target.CellFace.X,
            target.CellFace.Y,
            target.CellFace.Z,
            Terrain.ReplaceData(currentValue, m_data));
    }

    private static bool IsValidPistonEdit(int currentData, int proposedData) {
        const int immutableMask = 0x39;
        return IsValidAuthoritativeState(proposedData)
            && (proposedData & immutableMask) == (currentData & immutableMask);
    }

    internal static bool IsValidAuthoritativeState(int data) {
        const int knownMask = 0x3FFF;
        return (data & ~knownMask) == 0
            && PistonBlock.GetMode(data) <= PistonMode.StrictPulling
            && PistonBlock.GetMaxExtension(data) <= SubsystemPistonBlockBehavior.PistonMaxExtension
            && PistonBlock.GetPullCount(data) <= SubsystemPistonBlockBehavior.PistonMaxMovedBlocks
            && PistonBlock.GetSpeed(data) <= SubsystemPistonBlockBehavior.PistonMaxSpeedSetting;
    }
}

