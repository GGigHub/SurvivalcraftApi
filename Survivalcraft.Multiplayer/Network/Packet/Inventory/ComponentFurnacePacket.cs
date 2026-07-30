using GameEntitySystem;

namespace Game.Network;

public class ComponentFurnacePacket : Packet {
    public const int MaximumFurnaceCount = 1024;

    public override byte ID => (byte)PacketType.Furnace;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;

    public struct FurnacePacketData {
        public int inventoryID;
        public int entityID;
        public float fireTimeRemaining;
        public float smeltingProgress;
        public float heatLevel;
        public long lifecycleRevision;
        public long revision;
    }

    public readonly List<FurnacePacketData> furnaceDataList = [];

    public ComponentFurnacePacket() {
    }

    public ComponentFurnacePacket(IEnumerable<ComponentFurnace> componentFurnaces) {
        ArgumentNullException.ThrowIfNull(componentFurnaces);
        foreach (ComponentFurnace componentFurnace in componentFurnaces) {
            furnaceDataList.Add(CreateData(componentFurnace));
        }
        ValidateCount(furnaceDataList.Count);
    }

    public ComponentFurnacePacket(ComponentFurnace componentFurnace, long revision = -1) {
        ArgumentNullException.ThrowIfNull(componentFurnace);
        FurnacePacketData data = CreateData(componentFurnace);
        if (revision >= 0) {
            data.revision = revision;
        }
        furnaceDataList.Add(data);
    }

    public override void Serialize(PacketSerializer archive) {
        int count = archive.IsWriting ? furnaceDataList.Count : 0;
        archive.Value(ref count);
        ValidateCount(count);
        if (archive.IsReading) {
            furnaceDataList.Clear();
            for (int i = 0; i < count; i++) {
                FurnacePacketData data = new();
                SerializeData(archive, ref data);
                furnaceDataList.Add(data);
            }
        }
        else {
            for (int i = 0; i < count; i++) {
                FurnacePacketData data = furnaceDataList[i];
                SerializeData(archive, ref data);
            }
        }
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        Project project = GameManager.Project;
        if (project == null) {
            return;
        }
        foreach (FurnacePacketData data in furnaceDataList) {
            if (!IsValid(data)) {
                continue;
            }
            Entity entity = project.FindEntity(data.entityID);
            if (!EntityLifecycleTracker.CanApplyState(
                    project,
                    data.entityID,
                    data.lifecycleRevision)) {
                if (EntityLifecycleTracker.ShouldRequestLoad(
                        project,
                        data.entityID,
                        data.lifecycleRevision,
                        entity != null)) {
                    NetworkManager.Queue(new EntitySyncRequestPacket([data.entityID]));
                }
                NetworkStateRevisionTracker.RecordIgnoredLifecycleState(project);
                continue;
            }
            ComponentFurnace furnace = entity?.FindComponent<ComponentFurnace>();
            if (furnace == null || furnace.Id != data.inventoryID) {
                continue;
            }
            NetworkStateRevisionDecision decision = NetworkStateRevisionTracker.Receive(
                project,
                NetworkStateRevisionDomain.Furnace,
                data.inventoryID,
                data.revision,
                forceRefresh: true);
            if (!decision.Apply) {
                continue;
            }
            furnace.m_smeltingProgress = data.smeltingProgress;
            furnace.m_fireTimeRemaining = data.fireTimeRemaining;
            furnace.m_heatLevel = data.heatLevel;
        }
    }

    private static FurnacePacketData CreateData(ComponentFurnace furnace) {
        if (furnace.Entity?.Id <= 0 || furnace.Id <= 0) {
            throw new ArgumentException("Furnace must belong to a tracked entity and inventory.", nameof(furnace));
        }
        return new FurnacePacketData {
            inventoryID = furnace.Id,
            entityID = furnace.Entity.Id,
            fireTimeRemaining = furnace.FireTimeRemaining,
            smeltingProgress = furnace.SmeltingProgress,
            heatLevel = furnace.HeatLevel,
            lifecycleRevision = EntityLifecycleTracker.GetAuthoritativeRevision(
                furnace.Entity.Project,
                furnace.Entity.Id),
            revision = NetworkStateRevisionTracker.GetAuthoritativeRevision(
                furnace.Project,
                NetworkStateRevisionDomain.Furnace,
                furnace.Id)
        };
    }

    private static void SerializeData(PacketSerializer archive, ref FurnacePacketData data) {
        archive.Value(ref data.inventoryID);
        archive.Value(ref data.entityID);
        archive.Value(ref data.fireTimeRemaining);
        archive.Value(ref data.smeltingProgress);
        archive.Value(ref data.heatLevel);
        archive.Value(ref data.lifecycleRevision);
        archive.Value(ref data.revision);
    }

    private static bool IsValid(FurnacePacketData data) =>
        data.inventoryID > 0
        && data.entityID > 0
        && data.lifecycleRevision >= 0
        && data.revision >= 0
        && float.IsFinite(data.fireTimeRemaining)
        && data.fireTimeRemaining >= 0f
        && float.IsFinite(data.smeltingProgress)
        && data.smeltingProgress is >= 0f and <= 1f
        && float.IsFinite(data.heatLevel)
        && data.heatLevel >= 0f;

    private static void ValidateCount(int count) {
        if (count is < 0 or > MaximumFurnaceCount) {
            throw new InvalidDataException(
                $"Invalid furnace state count {count}; maximum is {MaximumFurnaceCount}.");
        }
    }
}
