using Engine;
using GameEntitySystem;

namespace Game.Network;

/// <summary>
/// Session-owned mount requests and lifecycle-bound authoritative mount state.
/// </summary>
public class ComponentMountPacket : Packet {
    public enum EventType : byte {
        MountRequest,
        DismountRequest,
        State
    }

    public enum ResultCode : byte {
        None,
        Accepted,
        InvalidRequest,
        OwnershipMismatch,
        StaleState,
        RiderBusy,
        MountUnavailable,
        MountOccupied,
        OutOfRange
    }

    public override byte ID => (byte)PacketType.Mount;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;

    public EventType m_type;
    public ResultCode m_result;
    public int m_riderEntityId;
    public int m_mountEntityId;
    public long m_riderLifecycleRevision;
    public long m_mountLifecycleRevision;
    public long m_revision;
    public long m_requestId;

    public ComponentMountPacket() {
    }

    public ComponentMountPacket(
        ComponentRider rider,
        ComponentMount mount,
        bool isRequest = false) {
        if (isRequest) {
            InitializeRequest(rider, mount, EventType.MountRequest);
        }
        else {
            InitializeState(rider, mount, ResultCode.Accepted, requestId: 0, revision: -1);
        }
    }

    public ComponentMountPacket(ComponentRider rider, bool isRequest = false) {
        if (isRequest) {
            InitializeRequest(rider, rider.AuthoritativeMount, EventType.DismountRequest);
        }
        else {
            InitializeState(rider, null, ResultCode.Accepted, requestId: 0, revision: -1);
        }
    }

    public static ComponentMountPacket CreateState(
        ComponentRider rider,
        ComponentMount mount,
        long revision = -1,
        ResultCode result = ResultCode.Accepted,
        long requestId = 0) {
        ComponentMountPacket packet = new();
        packet.InitializeState(rider, mount, result, requestId, revision);
        return packet;
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Enum(ref m_type);
        archive.Enum(ref m_result);
        archive.Value(ref m_riderEntityId);
        archive.Value(ref m_mountEntityId);
        archive.Value(ref m_riderLifecycleRevision);
        archive.Value(ref m_mountLifecycleRevision);
        archive.Value(ref m_revision);
        archive.Value(ref m_requestId);
    }

    public override void Handle(bool isServer) {
        if (!Enum.IsDefined(m_type) || !Enum.IsDefined(m_result)) {
            return;
        }
        if (isServer) {
            if (m_type is EventType.MountRequest or EventType.DismountRequest) {
                HandleRequest();
            }
            return;
        }
        if (m_type == EventType.State) {
            ApplyState();
        }
    }

    private void InitializeRequest(
        ComponentRider rider,
        ComponentMount mount,
        EventType type) {
        ArgumentNullException.ThrowIfNull(rider);
        Project project = rider.Entity.Project;
        bool client = NetworkManager.IsClientRunning;
        m_type = type;
        m_result = ResultCode.None;
        m_riderEntityId = rider.Entity.Id;
        m_mountEntityId = mount?.Entity?.Id ?? 0;
        m_riderLifecycleRevision = LifecycleRevision(project, m_riderEntityId, client);
        m_mountLifecycleRevision = m_mountEntityId > 0
            ? LifecycleRevision(project, m_mountEntityId, client)
            : -1;
        m_revision = client
            ? NetworkStateRevisionTracker.GetAppliedRevision(
                project,
                NetworkStateRevisionDomain.Mount,
                m_riderEntityId)
            : NetworkStateRevisionTracker.GetAuthoritativeRevision(
                project,
                NetworkStateRevisionDomain.Mount,
                m_riderEntityId);
        m_requestId = NetworkMutationRequestTracker.CreateRequestId();
    }

    private void InitializeState(
        ComponentRider rider,
        ComponentMount mount,
        ResultCode result,
        long requestId,
        long revision) {
        ArgumentNullException.ThrowIfNull(rider);
        Project project = rider.Entity.Project;
        m_type = EventType.State;
        m_result = result;
        m_riderEntityId = rider.Entity.Id;
        m_mountEntityId = mount?.Entity?.Id ?? 0;
        m_riderLifecycleRevision = EntityLifecycleTracker.GetAuthoritativeRevision(
            project,
            m_riderEntityId);
        m_mountLifecycleRevision = m_mountEntityId > 0
            ? EntityLifecycleTracker.GetAuthoritativeRevision(project, m_mountEntityId)
            : -1;
        m_revision = revision >= 0
            ? revision
            : NetworkStateRevisionTracker.GetAuthoritativeRevision(
                project,
                NetworkStateRevisionDomain.Mount,
                m_riderEntityId);
        m_requestId = requestId;
    }

    private void HandleRequest() {
        ComponentPlayer player = From?.GetPlayerData()?.ComponentPlayer;
        ComponentRider rider = player?.Entity?.FindComponent<ComponentRider>();
        if (From == null
            || From.NetworkState != NetworkState.Playing
            || rider == null
            || rider.Entity.Id <= 0
            || m_requestId <= 0) {
            return;
        }
        if (!NetworkMutationRequestTracker.TryAccept(
                From,
                NetworkMutationDomain.PlayerMount,
                m_requestId)) {
            return;
        }
        if (m_riderEntityId != rider.Entity.Id) {
            QueueCurrentState(rider, ResultCode.OwnershipMismatch);
            return;
        }

        Project project = rider.Entity.Project;
        long riderLifecycle = EntityLifecycleTracker.GetAuthoritativeRevision(
            project,
            rider.Entity.Id);
        long currentRevision = NetworkStateRevisionTracker.GetAuthoritativeRevision(
            project,
            NetworkStateRevisionDomain.Mount,
            rider.Entity.Id);
        if (m_riderLifecycleRevision != riderLifecycle
            || m_revision != currentRevision) {
            QueueCurrentState(rider, ResultCode.StaleState);
            return;
        }

        if (m_type == EventType.MountRequest) {
            HandleMountRequest(rider, project);
        }
        else {
            HandleDismountRequest(rider, project);
        }
    }

    private void HandleMountRequest(ComponentRider rider, Project project) {
        Entity mountEntity = m_mountEntityId > 0
            ? project.FindEntity(m_mountEntityId)
            : null;
        ComponentMount mount = mountEntity?.FindComponent<ComponentMount>();
        if (mount == null
            || m_mountLifecycleRevision != EntityLifecycleTracker.GetAuthoritativeRevision(
                project,
                m_mountEntityId)) {
            QueueCurrentState(rider, ResultCode.MountUnavailable);
            return;
        }
        ResultCode validation = ValidateMount(rider, mount);
        if (validation != ResultCode.Accepted) {
            QueueCurrentState(rider, validation);
            return;
        }
        if (!rider.ApplyAuthoritativeMountState(mount, animate: true)) {
            QueueCurrentState(rider, ResultCode.MountUnavailable);
            return;
        }
        long revision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
            project,
            NetworkStateRevisionDomain.Mount,
            rider.Entity.Id);
        NetworkManager.Queue(CreateState(
            rider,
            mount,
            revision,
            ResultCode.Accepted,
            m_requestId));
    }

    private void HandleDismountRequest(ComponentRider rider, Project project) {
        ComponentMount currentMount = rider.AuthoritativeMount;
        if (rider.m_isAnimating) {
            QueueCurrentState(rider, ResultCode.RiderBusy);
            return;
        }
        if (currentMount == null
            || currentMount.Entity.Id != m_mountEntityId
            || m_mountLifecycleRevision != EntityLifecycleTracker.GetAuthoritativeRevision(
                project,
                currentMount.Entity.Id)) {
            QueueCurrentState(rider, ResultCode.StaleState);
            return;
        }
        if (!rider.ApplyAuthoritativeMountState(null, animate: true)) {
            QueueCurrentState(rider, ResultCode.MountUnavailable);
            return;
        }
        long revision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
            project,
            NetworkStateRevisionDomain.Mount,
            rider.Entity.Id);
        NetworkManager.Queue(CreateState(
            rider,
            null,
            revision,
            ResultCode.Accepted,
            m_requestId));
    }

    private void QueueCurrentState(ComponentRider rider, ResultCode result) {
        ComponentMountPacket state = CreateState(
            rider,
            rider.AuthoritativeMount,
            result: result,
            requestId: m_requestId);
        state.To = From;
        NetworkManager.Queue(state);
    }

    private static ResultCode ValidateMount(
        ComponentRider rider,
        ComponentMount mount) {
        ComponentBody riderBody = rider.ComponentCreature?.ComponentBody;
        ComponentBody mountBody = mount.ComponentBody;
        if (riderBody == null
            || mountBody == null
            || rider.m_isAnimating
            || rider.Mount != null) {
            return ResultCode.RiderBusy;
        }
        if (mount.Entity == rider.Entity
            || mountBody.IsChildOfBody(riderBody)) {
            return ResultCode.MountUnavailable;
        }
        ComponentRider occupyingRider = mount.Rider;
        if (occupyingRider != null && occupyingRider != rider) {
            return ResultCode.MountOccupied;
        }
        ComponentHealth riderHealth = rider.Entity.FindComponent<ComponentHealth>();
        ComponentHealth mountHealth = mount.Entity.FindComponent<ComponentHealth>();
        if (riderHealth?.Health <= 0f || mountHealth?.Health <= 0f) {
            return ResultCode.MountUnavailable;
        }
        if (mountBody.Velocity.LengthSquared() >= 1f
            || !IsFinite(riderBody.Position)
            || !IsFinite(mountBody.Position)
            || !float.IsFinite(mount.MountAllowToStartRange)
            || mount.MountAllowToStartRange <= 0f) {
            return ResultCode.MountUnavailable;
        }
        float maximumDistance = MathF.Min(
            rider.DetectSurroundingMountRange,
            mount.MountAllowToStartRange) + 0.5f;
        Vector3 mountPoint = mountBody.Position
            + Vector3.Transform(mount.MountOffset, mountBody.Rotation);
        if (Vector3.DistanceSquared(riderBody.Position, mountPoint)
            > maximumDistance * maximumDistance) {
            return ResultCode.OutOfRange;
        }
        // Preserve the mod hook's explicit deny contract without depending on
        // a replicated camera direction for the positive vanilla score.
        if (rider.ScoreMount(mount, maximumDistance) < 0f) {
            return ResultCode.MountUnavailable;
        }
        return ResultCode.Accepted;
    }

    private void ApplyState() {
        if (m_riderEntityId <= 0
            || m_mountEntityId < 0
            || m_riderLifecycleRevision < 0
            || m_revision < 0
            || m_result == ResultCode.None
            || (m_mountEntityId == 0) != (m_mountLifecycleRevision < 0)) {
            return;
        }
        Project project = GameManager.Project;
        if (project == null) {
            return;
        }
        List<int> missingEntityIds = [];
        Entity riderEntity = project.FindEntity(m_riderEntityId);
        if (!EntityLifecycleTracker.CanApplyState(
                project,
                m_riderEntityId,
                m_riderLifecycleRevision)) {
            if (EntityLifecycleTracker.ShouldRequestLoad(
                    project,
                    m_riderEntityId,
                    m_riderLifecycleRevision,
                    riderEntity != null)) {
                missingEntityIds.Add(m_riderEntityId);
            }
        }
        Entity mountEntity = null;
        if (m_mountEntityId > 0) {
            mountEntity = project.FindEntity(m_mountEntityId);
            if (!EntityLifecycleTracker.CanApplyState(
                    project,
                    m_mountEntityId,
                    m_mountLifecycleRevision)) {
                if (EntityLifecycleTracker.ShouldRequestLoad(
                        project,
                        m_mountEntityId,
                        m_mountLifecycleRevision,
                        mountEntity != null)) {
                    missingEntityIds.Add(m_mountEntityId);
                }
            }
        }
        if (missingEntityIds.Count > 0) {
            NetworkManager.Queue(new EntitySyncRequestPacket(missingEntityIds));
            return;
        }
        if (!EntityLifecycleTracker.CanApplyState(
                project,
                m_riderEntityId,
                m_riderLifecycleRevision)
            || m_mountEntityId > 0
                && !EntityLifecycleTracker.CanApplyState(
                    project,
                    m_mountEntityId,
                    m_mountLifecycleRevision)) {
            NetworkStateRevisionTracker.RecordIgnoredLifecycleState(project);
            return;
        }
        ComponentRider rider = riderEntity?.FindComponent<ComponentRider>();
        ComponentMount mount = mountEntity?.FindComponent<ComponentMount>();
        if (rider == null || m_mountEntityId > 0 && mount == null) {
            return;
        }
        NetworkStateRevisionDecision decision = NetworkStateRevisionTracker.Receive(
            project,
            NetworkStateRevisionDomain.Mount,
            m_riderEntityId,
            m_revision,
            forceRefresh: true);
        if (!decision.Apply) {
            return;
        }
        rider.ApplyAuthoritativeMountState(mount, animate: true);
        if (m_result != ResultCode.Accepted
            && rider.Entity.FindComponent<ComponentPlayer>() is ComponentPlayer player
            && player.PlayerData.IsMainPlayer) {
            player.ComponentGui?.DisplaySmallMessage(
                $"Mount request rejected ({m_result}).",
                Color.White,
                false,
                true);
        }
    }

    private static long LifecycleRevision(Project project, int entityId, bool client) =>
        client
            ? EntityLifecycleTracker.GetAppliedRevision(project, entityId)
            : EntityLifecycleTracker.GetAuthoritativeRevision(project, entityId);

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X)
        && float.IsFinite(value.Y)
        && float.IsFinite(value.Z);
}
