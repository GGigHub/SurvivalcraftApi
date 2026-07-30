using Engine;
using GameEntitySystem;

namespace Game.Network;

public class SubsystemSkyPacket : Packet {
    public enum PacketKind : byte {
        State,
        Request
    }

    public override byte ID => (byte)PacketType.Sky;
    public override NetworkState MinNeedState => NetworkState.Playing;
    public PacketKind m_kind;
    public Vector3 LightningStrikePosition;
    public Vector3 m_direction;
    public long m_revision;
    public long m_requestId;

    public SubsystemSkyPacket() {
    }

    public SubsystemSkyPacket(Vector3 position, long revision = -1) {
        m_kind = PacketKind.State;
        LightningStrikePosition = position;
        m_revision = revision >= 0
            ? revision
            : NetworkStateRevisionTracker.GetAuthoritativeRevision(
                GameManager.Project,
                NetworkStateRevisionDomain.Sky,
                SubsystemTimePacket.EnvironmentStateId);
    }

    public SubsystemSkyPacket(Vector3 position, Vector3 direction) {
        m_kind = PacketKind.Request;
        LightningStrikePosition = position;
        m_direction = direction;
        m_requestId = NetworkMutationRequestTracker.CreateRequestId();
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Enum(ref m_kind);
        archive.Value(ref LightningStrikePosition);
        archive.Value(ref m_direction);
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
            || !IsFinite(LightningStrikePosition)) {
            return;
        }
        Project project = GameManager.Project;
        SubsystemSky sky = project?.FindSubsystem<SubsystemSky>();
        if (project == null || sky == null) {
            return;
        }
        NetworkStateRevisionDecision decision = NetworkStateRevisionTracker.Receive(
            project,
            NetworkStateRevisionDomain.Sky,
            SubsystemTimePacket.EnvironmentStateId,
            m_revision);
        if (decision.Apply) {
            sky.MakeLightningStrike(LightningStrikePosition, true, true);
        }
    }

    private void HandleRequest() {
        Project project = GameManager.Project;
        SubsystemGameInfo info = project?.FindSubsystem<SubsystemGameInfo>();
        ComponentPlayer player = From?.GetPlayerData()?.ComponentPlayer;
        Vector3 authoritativeEye = player?.ComponentCreatureModel?.EyePosition ?? Vector3.Zero;
        float directionLengthSquared = m_direction.LengthSquared();
        if (m_kind != PacketKind.Request
            || project == null
            || info == null
            || From == null
            || From.NetworkState != NetworkState.Playing
            || player == null
            || !IsFinite(LightningStrikePosition)
            || !IsFinite(m_direction)
            || directionLengthSquared is < 0.25f or > 2.25f
            || !NetworkMutationRequestTracker.TryAccept(
                From,
                NetworkMutationDomain.EnvironmentLightning,
                m_requestId)
            || info.WorldSettings.GameMode != GameMode.Creative
            || Vector3.DistanceSquared(LightningStrikePosition, authoritativeEye) > 36f) {
            return;
        }
        project.FindSubsystem<SubsystemWeather>()
            ?.ManualLightingStrike(authoritativeEye, Vector3.Normalize(m_direction));
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X)
        && float.IsFinite(value.Y)
        && float.IsFinite(value.Z);
}
