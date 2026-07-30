using Engine;
using GameEntitySystem;

namespace Game.Network;

public class SubsystemWeatherPacket : Packet {
    public enum PacketKind : byte {
        State,
        Request
    }

    public override byte ID => (byte)PacketType.Weather;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;
    public PacketKind m_kind;
    public double m_precipitationStartTime;
    public double m_precipitationEndTime;
    public float m_precipitationRampTime;
    public float m_lightningIntensity;
    public int m_weatherType;
    public double m_fogStartTime;
    public double m_fogEndTime;
    public float m_fogRampTime;
    public float FogProgress;
    public bool m_hasPrecipitationSchedule;
    public bool m_hasFogSchedule;
    public long m_revision;
    public long m_requestId;

    public SubsystemWeatherPacket() {
    }

    public SubsystemWeatherPacket(int weatherType) {
        m_kind = PacketKind.Request;
        m_weatherType = weatherType;
        m_requestId = NetworkMutationRequestTracker.CreateRequestId();
    }

    public SubsystemWeatherPacket(SubsystemWeather weather, long revision = -1) {
        ArgumentNullException.ThrowIfNull(weather);
        m_kind = PacketKind.State;
        m_precipitationStartTime = Math.Max(0, weather.m_precipitationStartTime);
        m_precipitationEndTime = Math.Max(
            m_precipitationStartTime,
            weather.m_precipitationEndTime);
        m_precipitationRampTime = Math.Max(0.001f, weather.m_precipitationRampTime);
        m_lightningIntensity = Math.Clamp(weather.m_lightningIntensity, 0f, 4f);
        m_fogStartTime = Math.Max(0, weather.m_fogStartTime);
        m_fogEndTime = Math.Max(m_fogStartTime, weather.m_fogEndTime);
        m_fogRampTime = Math.Max(0.001f, weather.m_fogRampTime);
        FogProgress = MathUtils.Saturate(weather.FogProgress);
        m_hasPrecipitationSchedule = true;
        m_hasFogSchedule = true;
        m_revision = revision >= 0
            ? revision
            : NetworkStateRevisionTracker.GetAuthoritativeRevision(
                weather.Project,
                NetworkStateRevisionDomain.Weather,
                SubsystemTimePacket.EnvironmentStateId);
    }

    public SubsystemWeatherPacket(double start, double end, float light, float ramp = 25f) {
        m_kind = PacketKind.State;
        m_precipitationStartTime = start;
        m_precipitationEndTime = end;
        m_lightningIntensity = light;
        m_precipitationRampTime = ramp;
        m_hasPrecipitationSchedule = true;
    }

    public SubsystemWeatherPacket(double fogstart, float ramp, double fogend, float progress = 0f) {
        m_kind = PacketKind.State;
        m_fogStartTime = fogstart;
        m_fogRampTime = ramp;
        m_fogEndTime = fogend;
        FogProgress = progress;
        m_hasFogSchedule = true;
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Enum(ref m_kind);
        archive.Value(ref m_hasPrecipitationSchedule);
        archive.Value(ref m_hasFogSchedule);
        archive.Value(ref m_precipitationStartTime);
        archive.Value(ref m_precipitationEndTime);
        archive.Value(ref m_precipitationRampTime);
        archive.Value(ref m_lightningIntensity);
        archive.Value(ref m_weatherType);
        archive.Value(ref m_fogStartTime);
        archive.Value(ref m_fogRampTime);
        archive.Value(ref m_fogEndTime);
        archive.Value(ref FogProgress);
        archive.Value(ref m_revision);
        archive.Value(ref m_requestId);
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            HandleRequest();
            return;
        }
        if (m_kind != PacketKind.State || !ValidateState()) {
            return;
        }
        Project project = GameManager.Project;
        SubsystemWeather weather = project?.FindSubsystem<SubsystemWeather>();
        if (project == null || weather == null) {
            return;
        }
        NetworkStateRevisionDecision decision = NetworkStateRevisionTracker.Receive(
            project,
            NetworkStateRevisionDomain.Weather,
            SubsystemTimePacket.EnvironmentStateId,
            m_revision,
            forceRefresh: true);
        if (!decision.Apply) {
            return;
        }
        if (m_hasPrecipitationSchedule) {
            weather.m_precipitationStartTime = m_precipitationStartTime;
            weather.m_precipitationEndTime = m_precipitationEndTime;
            weather.m_precipitationRampTime = m_precipitationRampTime;
            weather.m_lightningIntensity = m_lightningIntensity;
        }
        if (m_hasFogSchedule) {
            weather.m_fogStartTime = m_fogStartTime;
            weather.m_fogEndTime = m_fogEndTime;
            weather.m_fogRampTime = m_fogRampTime;
            weather.FogProgress = FogProgress;
        }
    }

    private void HandleRequest() {
        Project project = GameManager.Project;
        SubsystemGameInfo info = project?.FindSubsystem<SubsystemGameInfo>();
        SubsystemWeather weather = project?.FindSubsystem<SubsystemWeather>();
        if (m_kind != PacketKind.Request
            || project == null
            || info == null
            || weather == null
            || From == null
            || From.NetworkState != NetworkState.Playing
            || From.GetPlayerData()?.ComponentPlayer == null
            || m_weatherType is < 1 or > 4) {
            return;
        }
        if (!NetworkMutationRequestTracker.TryAccept(
                From,
                NetworkMutationDomain.EnvironmentWeather,
                m_requestId)) {
            return;
        }
        if (info.WorldSettings.GameMode != GameMode.Creative) {
            QueueAuthoritativeState(project, weather, From);
            return;
        }
        switch (m_weatherType) {
            case 1:
                weather.ManualPrecipitationEnd();
                break;
            case 2:
                weather.ManualPrecipitationStart();
                break;
            case 3:
                weather.ManualFogEnd();
                break;
            case 4:
                weather.ManualFogStart();
                break;
        }
        long revision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
            project,
            NetworkStateRevisionDomain.Weather,
            SubsystemTimePacket.EnvironmentStateId);
        NetworkManager.Queue(new SubsystemWeatherPacket(weather, revision));
    }

    private static void QueueAuthoritativeState(
        Project project,
        SubsystemWeather weather,
        ClientSession to) {
        long revision = NetworkStateRevisionTracker.GetAuthoritativeRevision(
            project,
            NetworkStateRevisionDomain.Weather,
            SubsystemTimePacket.EnvironmentStateId);
        NetworkManager.Queue(new SubsystemWeatherPacket(weather, revision) { To = to });
    }

    private bool ValidateState() {
        if (m_revision < 0 || m_weatherType != 0) {
            return false;
        }
        if (m_hasPrecipitationSchedule
            && (!double.IsFinite(m_precipitationStartTime)
                || m_precipitationStartTime < 0
                || !IsValidEnd(m_precipitationEndTime, m_precipitationStartTime)
                || !float.IsFinite(m_precipitationRampTime)
                || m_precipitationRampTime <= 0
                || !float.IsFinite(m_lightningIntensity)
                || m_lightningIntensity is < 0f or > 4f)) {
            return false;
        }
        return !m_hasFogSchedule
            || double.IsFinite(m_fogStartTime)
            && m_fogStartTime >= 0
            && IsValidEnd(m_fogEndTime, m_fogStartTime)
            && float.IsFinite(m_fogRampTime)
            && m_fogRampTime > 0
            && float.IsFinite(FogProgress)
            && FogProgress is >= 0f and <= 1f;
    }

    private static bool IsValidEnd(double end, double start) =>
        (double.IsFinite(end) || double.IsPositiveInfinity(end)) && end >= start;
}
