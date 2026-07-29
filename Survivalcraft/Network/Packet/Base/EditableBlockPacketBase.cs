using Engine;
using GameEntitySystem;

namespace Game.Network;

public enum EditableLogicPacketKind : byte {
    Request,
    State
}

public enum EditableLogicResultCode : byte {
    None,
    Accepted,
    InvalidRequest,
    DuplicateRequest,
    PermissionDenied,
    OutOfRange,
    TargetUnavailable,
    WrongBlock,
    StaleState,
    InvalidValue
}

public abstract class EditableBlockPacketBase : Packet {
    public const float MaximumBlockEditDistance = 6f;

    public EditableLogicPacketKind m_packetKind;
    public EditableLogicResultCode m_result;
    public long m_requestId;
    public long m_expectedRevision;
    public long m_revision;
    public long m_terrainRevision;

    protected readonly record struct EditableTarget(
        bool IsItem,
        int InventoryId,
        int SlotIndex,
        CellFace CellFace) {

        public NetworkLogicStateKey Key(NetworkLogicElementKind kind) =>
            NetworkLogicStateKey.Editable(
                kind,
                IsItem,
                InventoryId,
                SlotIndex,
                CellFace.Point);
    }

    protected void InitializeRequest(
        NetworkLogicElementKind kind,
        bool editAsItem,
        int inventoryId,
        int slotIndex,
        CellFace cellFace) {
        m_packetKind = EditableLogicPacketKind.Request;
        m_result = EditableLogicResultCode.None;
        m_requestId = NetworkMutationRequestTracker.CreateRequestId();
        m_revision = -1;
        Project project = GameManager.Project;
        if (project == null) {
            m_expectedRevision = 0;
            m_terrainRevision = -1;
            return;
        }
        EditableTarget target = new(
            editAsItem,
            inventoryId,
            slotIndex,
            cellFace);
        m_expectedRevision = NetworkLogicStateTracker.GetAppliedRevision(
            project,
            target.Key(kind));
        m_terrainRevision = editAsItem
            ? -1
            : GetClientTerrainRevision(project, cellFace.Point);
    }

    protected void InitializeState(
        NetworkLogicElementKind kind,
        bool editAsItem,
        int inventoryId,
        int slotIndex,
        CellFace cellFace,
        long revision,
        EditableLogicResultCode result = EditableLogicResultCode.Accepted,
        long requestId = 0) {
        m_packetKind = EditableLogicPacketKind.State;
        m_result = result;
        m_requestId = requestId;
        m_expectedRevision = -1;
        m_revision = revision;
        Project project = GameManager.Project;
        m_terrainRevision = editAsItem || project == null
            ? -1
            : GetServerTerrainRevision(project, cellFace.Point);
    }

    protected static EditableTarget Target(
        bool editAsItem,
        int inventoryId,
        int slotIndex,
        CellFace cellFace) =>
        new(editAsItem, inventoryId, slotIndex, cellFace);

    protected void SerializeMutationEnvelope(PacketSerializer archive) {
        archive.Enum(ref m_packetKind);
        archive.Enum(ref m_result);
        archive.Value(ref m_requestId);
        archive.Value(ref m_expectedRevision);
        archive.Value(ref m_revision);
        archive.Value(ref m_terrainRevision);
    }

    public static void SerializeEditableTarget(PacketSerializer archive, ref bool editAsItem, ref int inventoryId, ref int slotIndex, ref CellFace cellFace) {
        archive.Value(ref editAsItem);
        if (editAsItem) {
            archive.Value(ref inventoryId);
            archive.Value(ref slotIndex);
        }
        else {
            archive.Value(ref cellFace);
        }
    }

    public static void SerializeOptionalId(PacketSerializer archive, ref int? id) {
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

    public static void SerializeByteArray(PacketSerializer archive, ref byte[] data) {
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

    protected EditableLogicResultCode ValidateServerRequest(
        NetworkLogicElementKind kind,
        EditableTarget target,
        int expectedContents,
        out IInventory inventory,
        out int currentValue) =>
        ValidateServerRequest(
            kind,
            target,
            value => Terrain.ExtractContents(value) == expectedContents,
            out inventory,
            out currentValue);

    protected EditableLogicResultCode ValidateServerRequest(
        NetworkLogicElementKind kind,
        EditableTarget target,
        Func<int, bool> isExpectedValue,
        out IInventory inventory,
        out int currentValue) {
        ArgumentNullException.ThrowIfNull(isExpectedValue);
        inventory = null;
        currentValue = 0;
        Project project = GameManager.Project;
        Point3 terrainPoint = target.CellFace.Point;
        if (project != null
            && !target.IsItem
            && terrainPoint.Y >= 0
            && terrainPoint.Y < TerrainChunk.Height) {
            SubsystemTerrain authoritativeTerrain = project.FindSubsystem<SubsystemTerrain>();
            if (authoritativeTerrain != null) {
                currentValue = authoritativeTerrain.Terrain.GetCellValue(
                    terrainPoint.X,
                    terrainPoint.Y,
                    terrainPoint.Z);
            }
        }
        if (m_packetKind != EditableLogicPacketKind.Request
            || m_result != EditableLogicResultCode.None
            || !Enum.IsDefined(kind)
            || kind == NetworkLogicElementKind.None
            || From == null
            || From.NetworkState != NetworkState.Playing
            || m_requestId <= 0
            || m_expectedRevision < 0
            || project == null) {
            return EditableLogicResultCode.InvalidRequest;
        }
        if (!NetworkMutationRequestTracker.TryAccept(
                From,
                NetworkMutationDomain.LogicEdit,
                m_requestId)) {
            return EditableLogicResultCode.DuplicateRequest;
        }
        NetworkLogicStateKey key = target.Key(kind);

        if (target.IsItem) {
            SubsystemInventories inventories = project.FindSubsystem<SubsystemInventories>();
            inventory = inventories?.GetInventoryById(target.InventoryId);
            if (inventory == null
                || !InventoryPacketHelpers.IsSlotIndexValid(inventory, target.SlotIndex)) {
                return EditableLogicResultCode.TargetUnavailable;
            }
            if (!InventoryPacketHelpers.CanSessionAccessInventory(inventory, From)) {
                return EditableLogicResultCode.PermissionDenied;
            }
            currentValue = inventory.GetSlotValue(target.SlotIndex);
            long itemRevision = NetworkLogicStateTracker.GetAuthoritativeRevision(
                project,
                key);
            if (m_expectedRevision != itemRevision) {
                return EditableLogicResultCode.StaleState;
            }
            return isExpectedValue(currentValue)
                ? EditableLogicResultCode.Accepted
                : EditableLogicResultCode.WrongBlock;
        }

        Point3 point = terrainPoint;
        if (target.CellFace.Face is < 0 or > 5
            || point.Y < 0
            || point.Y >= TerrainChunk.Height) {
            return EditableLogicResultCode.InvalidRequest;
        }
        long currentRevision = NetworkLogicStateTracker.GetAuthoritativeRevision(
            project,
            key);
        if (m_expectedRevision != currentRevision) {
            return EditableLogicResultCode.StaleState;
        }
        ComponentPlayer actor = From.GetPlayerData()?.ComponentPlayer;
        if (actor?.ComponentBody == null) {
            return EditableLogicResultCode.PermissionDenied;
        }
        Vector3 targetPosition = new Vector3(point) + new Vector3(0.5f);
        if (Vector3.DistanceSquared(actor.ComponentBody.Position, targetPosition)
            > MaximumBlockEditDistance * MaximumBlockEditDistance) {
            return EditableLogicResultCode.OutOfRange;
        }
        long terrainRevision = GetServerTerrainRevision(project, point);
        if (m_terrainRevision < 0 || m_terrainRevision != terrainRevision) {
            return EditableLogicResultCode.StaleState;
        }
        return isExpectedValue(currentValue)
            ? EditableLogicResultCode.Accepted
            : EditableLogicResultCode.WrongBlock;
    }

    protected bool TryAcceptClientState(
        NetworkLogicElementKind kind,
        EditableTarget target,
        int expectedContents,
        out IInventory inventory,
        out int currentValue) =>
        TryAcceptClientState(
            kind,
            target,
            value => Terrain.ExtractContents(value) == expectedContents,
            out inventory,
            out currentValue);

    protected bool TryAcceptClientState(
        NetworkLogicElementKind kind,
        EditableTarget target,
        Func<int, bool> isExpectedValue,
        out IInventory inventory,
        out int currentValue) {
        ArgumentNullException.ThrowIfNull(isExpectedValue);
        inventory = null;
        currentValue = 0;
        if (m_packetKind != EditableLogicPacketKind.State
            || m_result is not (EditableLogicResultCode.Accepted
                or EditableLogicResultCode.DuplicateRequest
                or EditableLogicResultCode.OutOfRange
                or EditableLogicResultCode.StaleState)
            || !Enum.IsDefined(m_result)
            || m_revision < 0
            || GameManager.Project == null) {
            return false;
        }
        Project project = GameManager.Project;
        if (target.IsItem) {
            inventory = project.FindSubsystem<SubsystemInventories>()
                ?.GetInventoryById(target.InventoryId);
            if (inventory == null
                || !InventoryPacketHelpers.IsSlotIndexValid(inventory, target.SlotIndex)) {
                return false;
            }
            currentValue = inventory.GetSlotValue(target.SlotIndex);
            if (!isExpectedValue(currentValue)) {
                return false;
            }
        }
        else {
            Point3 point = target.CellFace.Point;
            if (target.CellFace.Face is < 0 or > 5
                || point.Y < 0
                || point.Y >= TerrainChunk.Height) {
                return false;
            }
            long appliedTerrainRevision = GetClientTerrainRevision(project, point);
            if (m_terrainRevision >= 0
                && appliedTerrainRevision > m_terrainRevision) {
                return false;
            }
            SubsystemTerrain terrain = project.FindSubsystem<SubsystemTerrain>();
            currentValue = terrain.Terrain.GetCellValue(point.X, point.Y, point.Z);
            if (!isExpectedValue(currentValue)) {
                if (m_terrainRevision > appliedTerrainRevision) {
                    DeferredEditableLogicStateQueue.Enqueue(
                        project,
                        target.Key(kind),
                        this);
                }
                return false;
            }
        }
        return NetworkLogicStateTracker.Receive(
            project,
            target.Key(kind),
            m_revision,
            forceRefresh: m_requestId == 0
                && m_result == EditableLogicResultCode.Accepted).Apply;
    }

    protected long PrepareAuthoritativeState(
        NetworkLogicElementKind kind,
        EditableTarget target,
        EditableLogicResultCode result = EditableLogicResultCode.Accepted,
        bool advanceRevision = true) {
        Project project = GameManager.Project
            ?? throw new InvalidOperationException("Cannot prepare logic state without a project.");
        NetworkLogicStateKey key = target.Key(kind);
        long revision = advanceRevision
            ? NetworkLogicStateTracker.AdvanceAuthoritativeRevision(project, key)
            : NetworkLogicStateTracker.GetAuthoritativeRevision(project, key);
        InitializeState(
            kind,
            target.IsItem,
            target.InventoryId,
            target.SlotIndex,
            target.CellFace,
            revision,
            result,
            m_requestId);
        return revision;
    }

    protected void QueueAuthoritativeInventoryState(IInventory inventory) {
        if (inventory != null) {
            InventoryPacketHelpers.QueueInventorySync(inventory);
        }
    }

    private static long GetServerTerrainRevision(Project project, Point3 point) {
        SubsystemTerrain terrain = project.FindSubsystem<SubsystemTerrain>();
        return terrain == null
            ? -1
            : TerrainRevisionTracker.GetAuthoritativeRevision(
                terrain,
                point.X >> 4,
                point.Z >> 4);
    }

    private static long GetClientTerrainRevision(Project project, Point3 point) {
        SubsystemTerrain terrain = project.FindSubsystem<SubsystemTerrain>();
        return terrain == null
            ? -1
            : TerrainRevisionTracker.GetAppliedRevision(
                terrain,
                point.X >> 4,
                point.Z >> 4);
    }
}
