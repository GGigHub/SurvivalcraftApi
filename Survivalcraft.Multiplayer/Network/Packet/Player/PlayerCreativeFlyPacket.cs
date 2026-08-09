using Engine;
using GameEntitySystem;

namespace Game.Network;

public enum CreativeFlyResultCode : byte {
    None,
    Accepted,
    AlreadyApplied,
    InvalidRequest,
    InvalidGameMode,
    Mounted,
    StaleRequest,
    ClientStateUnavailable
}

public sealed class PlayerCreativeFlyPacket : PlayerPacketBase {
    private enum EventType : byte {
        Request,
        State
    }

    private EventType m_eventType;
    private long m_requestId;
    private CreativeFlyResultCode m_resultCode;
    private bool m_isCreativeFly;
    private int m_entityId;
    private long m_lifecycleRevision;
    private long m_revision;

    public override byte ID => (byte)PacketType.PlayerCreativeFly;

    public long RequestId => m_requestId;

    public PlayerCreativeFlyPacket() { }

    public PlayerCreativeFlyPacket(ComponentPlayer player, long requestId = 0) {
        ArgumentNullException.ThrowIfNull(player);
        m_eventType = EventType.Request;
        m_fromPlayerIndex = player.PlayerData.PlayerIndex;
        m_requestId = requestId > 0
            ? requestId
            : NetworkMutationRequestTracker.CreateRequestId();
        m_isCreativeFly = player.ComponentLocomotion.IsCreativeFlyEnabled;
        CreativeFlyResultTracker.TryReserve(m_requestId);
    }

    public static PlayerCreativeFlyPacket CreateState(
        ComponentPlayer player,
        bool enabled,
        CreativeFlyResultCode resultCode = CreativeFlyResultCode.Accepted,
        long requestId = 0,
        long revision = -1) {
        ArgumentNullException.ThrowIfNull(player);
        if (player.Entity.Id <= 0) {
            throw new ArgumentOutOfRangeException(nameof(player), "Player entity must have a positive ID.");
        }
        Project project = player.Entity.Project;
        return new PlayerCreativeFlyPacket {
            m_eventType = EventType.State,
            m_fromPlayerIndex = player.PlayerData.PlayerIndex,
            m_requestId = requestId,
            m_resultCode = resultCode,
            m_isCreativeFly = enabled,
            m_entityId = player.Entity.Id,
            m_lifecycleRevision = EntityLifecycleTracker.GetAuthoritativeRevision(
                project,
                player.Entity.Id),
            m_revision = revision >= 0
                ? revision
                : NetworkStateRevisionTracker.GetAuthoritativeRevision(
                    project,
                    NetworkStateRevisionDomain.CreativeFly,
                    player.Entity.Id)
        };
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Enum(ref m_eventType);
        if (!Enum.IsDefined(m_eventType)) {
            throw new InvalidDataException("Unknown creative-fly event type.");
        }
        SerializePlayerId(archive, null);
        archive.Value(ref m_requestId);
        switch (m_eventType) {
            case EventType.Request:
                archive.Value(ref m_isCreativeFly);
                break;
            case EventType.State:
                archive.Enum(ref m_resultCode);
                if (!Enum.IsDefined(m_resultCode)
                    || m_resultCode == CreativeFlyResultCode.None) {
                    throw new InvalidDataException("Unknown creative-fly result code.");
                }
                archive.Value(ref m_isCreativeFly);
                archive.Value(ref m_entityId);
                archive.Value(ref m_lifecycleRevision);
                archive.Value(ref m_revision);
                break;
            default:
                throw new InvalidDataException("Unknown creative-fly event type.");
        }
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            HandleServerRequest();
        }
        else {
            HandleClientState();
        }
    }

    private void HandleServerRequest() {
        if (m_eventType != EventType.Request || From == null) {
            return;
        }
        PlayerData playerData = From.GetPlayerData();
        ComponentPlayer player = playerData?.ComponentPlayer;
        if (player == null
            || From.NetworkState != NetworkState.Playing
            || m_requestId <= 0
            || m_fromPlayerIndex != From.PlayerIndex
            || player.PlayerData.PlayerIndex != m_fromPlayerIndex
            || player.Entity.Id <= 0) {
            if (player != null && player.Entity.Id > 0) {
                QueueCurrentState(player, CreativeFlyResultCode.InvalidRequest);
            }
            return;
        }

        NetworkMutationSequenceDecision sequence =
            NetworkMutationRequestTracker.ReceiveLatest(
                From,
                NetworkMutationDomain.PlayerCreativeFly,
                m_requestId);
        if (sequence == NetworkMutationSequenceDecision.Duplicate) {
            return;
        }
        if (sequence != NetworkMutationSequenceDecision.Accepted) {
            QueueCurrentState(player, sequence == NetworkMutationSequenceDecision.Stale
                ? CreativeFlyResultCode.StaleRequest
                : CreativeFlyResultCode.InvalidRequest);
            return;
        }

        SubsystemGameInfo gameInfo = GameManager.Project?.FindSubsystem<SubsystemGameInfo>();
        if (gameInfo?.WorldSettings?.GameMode != GameMode.Creative) {
            QueueCurrentState(player, CreativeFlyResultCode.InvalidGameMode);
            return;
        }
        if (player.ComponentRider?.Mount != null) {
            QueueCurrentState(player, CreativeFlyResultCode.Mounted);
            return;
        }

        bool changed = player.ComponentLocomotion.IsCreativeFlyEnabled != m_isCreativeFly;
        player.ComponentLocomotion.IsCreativeFlyEnabled = m_isCreativeFly;
        long revision = changed
            ? NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
                player.Entity.Project,
                NetworkStateRevisionDomain.CreativeFly,
                player.Entity.Id)
            : NetworkStateRevisionTracker.GetAuthoritativeRevision(
                player.Entity.Project,
                NetworkStateRevisionDomain.CreativeFly,
                player.Entity.Id);
        NetworkManager.Queue(CreateState(
            player,
            m_isCreativeFly,
            changed ? CreativeFlyResultCode.Accepted : CreativeFlyResultCode.AlreadyApplied,
            m_requestId,
            revision));
    }

    private void QueueCurrentState(
        ComponentPlayer player,
        CreativeFlyResultCode resultCode) {
        PlayerCreativeFlyPacket state = CreateState(
            player,
            player.ComponentLocomotion.IsCreativeFlyEnabled,
            resultCode,
            m_requestId);
        state.To = From;
        NetworkManager.Queue(state);
    }

    private void HandleClientState() {
        if (m_eventType != EventType.State
            || m_requestId < 0
            || !Enum.IsDefined(m_resultCode)
            || m_resultCode == CreativeFlyResultCode.None
            || m_entityId <= 0
            || m_lifecycleRevision < 0
            || m_revision < 0) {
            return;
        }
        Project project = GameManager.Project;
        PlayerData playerData = project?.FindSubsystem<SubsystemPlayers>()
            ?.FindPlayerData(player => player.PlayerIndex == m_fromPlayerIndex);
        Entity entity = project?.FindEntity(m_entityId);
        if (project == null
            || playerData?.ComponentPlayer == null
            || playerData.ComponentPlayer.Entity.Id != m_entityId
            || !EntityLifecycleTracker.CanApplyState(
                project,
                m_entityId,
                m_lifecycleRevision)) {
            if (project != null
                && EntityLifecycleTracker.ShouldRequestLoad(
                    project,
                    m_entityId,
                    m_lifecycleRevision,
                    entity != null)) {
                NetworkManager.Queue(new EntitySyncRequestPacket([m_entityId]));
            }
            if (m_requestId > 0 && playerData?.IsMainPlayer == true) {
                CreativeFlyResultTracker.Complete(new NetworkCreativeFlyResult(
                    m_requestId,
                    CreativeFlyResultCode.ClientStateUnavailable,
                    m_fromPlayerIndex,
                    m_entityId,
                    m_isCreativeFly,
                    m_revision));
            }
            return;
        }

        NetworkStateRevisionDecision decision = NetworkStateRevisionTracker.Receive(
            project,
            NetworkStateRevisionDomain.CreativeFly,
            m_entityId,
            m_revision,
            forceRefresh: true);
        if (!decision.Apply) {
            return;
        }
        playerData.ComponentPlayer.ComponentLocomotion.IsCreativeFlyEnabled = m_isCreativeFly;
        if (m_requestId > 0 && playerData.IsMainPlayer) {
            CreativeFlyResultTracker.Complete(new NetworkCreativeFlyResult(
                m_requestId,
                m_resultCode,
                m_fromPlayerIndex,
                m_entityId,
                m_isCreativeFly,
                m_revision));
        }
    }
}
