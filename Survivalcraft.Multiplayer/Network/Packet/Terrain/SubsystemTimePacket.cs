using Engine;
using GameEntitySystem;

namespace Game.Network;

public class SubsystemTimePacket : Packet {
    public const int EnvironmentStateId = 1;

    public enum PacketKind : byte {
        State,
        Request
    }

    public override byte ID => (byte)PacketType.Time;
    public override NetworkState MinNeedState => NetworkState.Playing;
    public PacketKind m_kind;
    public double m_time;
    public double m_timeOfDayOffset;
    public float m_timeOfYear;
    public long m_revision;
    public long m_requestId;

    public SubsystemTimePacket() {
    }

    public SubsystemTimePacket(
        double totalElapsedGameTime,
        double timeOfDayOffset,
        long revision = -1) {
        m_kind = PacketKind.State;
        m_time = totalElapsedGameTime;
        m_timeOfDayOffset = timeOfDayOffset;
        m_timeOfYear = GameManager.Project.FindSubsystem<SubsystemGameInfo>()
            ?.WorldSettings?.TimeOfYear ?? 0f;
        m_revision = revision >= 0
            ? revision
            : NetworkStateRevisionTracker.GetAuthoritativeRevision(
                GameManager.Project,
                NetworkStateRevisionDomain.Time,
                EnvironmentStateId);
    }

    public static SubsystemTimePacket CreateRequest(double timeOfDayOffset) => new() {
        m_kind = PacketKind.Request,
        m_timeOfDayOffset = timeOfDayOffset,
        m_requestId = NetworkMutationRequestTracker.CreateRequestId()
    };

    public override void Serialize(PacketSerializer archive) {
        archive.Enum(ref m_kind);
        archive.Value(ref m_time);
        archive.Value(ref m_timeOfDayOffset);
        archive.Value(ref m_timeOfYear);
        archive.Value(ref m_revision);
        archive.Value(ref m_requestId);
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            HandleRequest();
            return;
        }
        if (m_kind != PacketKind.State
            || m_revision < 0
            || !double.IsFinite(m_time)
            || m_time < 0
            || !double.IsFinite(m_timeOfDayOffset)
            || !float.IsFinite(m_timeOfYear)) {
            return;
        }
        Project project = GameManager.Project;
        if (project == null) {
            return;
        }
        NetworkStateRevisionDecision decision = NetworkStateRevisionTracker.Receive(
            project,
            NetworkStateRevisionDomain.Time,
            EnvironmentStateId,
            m_revision,
            forceRefresh: true);
        if (!decision.Apply) {
            return;
        }
        SubsystemGameInfo info = project.FindSubsystem<SubsystemGameInfo>();
        if (info == null) {
            return;
        }
        info.TotalElapsedGameTime = m_time;
        info.TimeOfDay.TimeOfDayOffset = m_timeOfDayOffset;
        info.WorldSettings.TimeOfYear = IntervalUtils.Normalize(m_timeOfYear);
        SubsystemTime subsystemTime = project.FindSubsystem<SubsystemTime>();
        if (subsystemTime != null) {
            subsystemTime.m_gameTime = m_time;
        }
    }

    private void HandleRequest() {
        Project project = GameManager.Project;
        SubsystemGameInfo info = project?.FindSubsystem<SubsystemGameInfo>();
        if (m_kind != PacketKind.Request
            || project == null
            || info == null
            || From == null
            || From.NetworkState != NetworkState.Playing
            || From.GetPlayerData()?.ComponentPlayer == null
            || !double.IsFinite(m_timeOfDayOffset)) {
            return;
        }
        if (!NetworkMutationRequestTracker.TryAccept(
                From,
                NetworkMutationDomain.EnvironmentTime,
                m_requestId)) {
            return;
        }
        if (info.WorldSettings.GameMode != GameMode.Creative
            || Math.Abs(m_timeOfDayOffset - info.TimeOfDay.TimeOfDayOffset) > 1.0001) {
            QueueAuthoritativeState(project, info, From);
            return;
        }
        info.TimeOfDay.TimeOfDayOffset = m_timeOfDayOffset;
        long revision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
            project,
            NetworkStateRevisionDomain.Time,
            EnvironmentStateId);
        NetworkManager.Queue(new SubsystemTimePacket(
            info.TotalElapsedGameTime,
            info.TimeOfDay.TimeOfDayOffset,
            revision));
    }

    private static void QueueAuthoritativeState(
        Project project,
        SubsystemGameInfo info,
        ClientSession to) {
        long revision = NetworkStateRevisionTracker.GetAuthoritativeRevision(
            project,
            NetworkStateRevisionDomain.Time,
            EnvironmentStateId);
        NetworkManager.Queue(new SubsystemTimePacket(
            info.TotalElapsedGameTime,
            info.TimeOfDay.TimeOfDayOffset,
            revision) { To = to });
    }
}
