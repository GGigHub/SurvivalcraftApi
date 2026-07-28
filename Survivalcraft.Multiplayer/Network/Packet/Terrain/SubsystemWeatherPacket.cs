using Engine;

namespace Game.Network;

public class SubsystemWeatherPacket : Packet {
    public override byte ID => (byte)PacketType.Weather;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;
    public double m_precipitationStartTime;
    public double m_precipitationEndTime;
    public float m_precipitationRampTime;
    public float m_lightningIntensity;
    public int m_weatherType = 0;
    public int FogType = 0;
    public double m_fogStartTime;
    public double m_fogEndTime;
    public float m_fogRampTime;
    public float FogProgress;
    public float m_intensity;
    public bool m_hasPrecipitationSchedule;
    public bool m_hasFogSchedule;

    public SubsystemWeatherPacket() {
    }

    public SubsystemWeatherPacket(int weatherType) {
        m_weatherType = weatherType;
    }

    public SubsystemWeatherPacket(float intensity) {
        m_intensity = intensity;
    }

    public SubsystemWeatherPacket(double start, double end, float light) {
        m_precipitationEndTime = end;
        m_precipitationStartTime = start;
        m_lightningIntensity = light;
        m_precipitationRampTime = 25f;
        m_hasPrecipitationSchedule = true;
    }

    public SubsystemWeatherPacket(double start, double end, float light, float ramp) {
        m_precipitationEndTime = end;
        m_precipitationStartTime = start;
        m_lightningIntensity = light;
        m_precipitationRampTime = ramp;
        m_hasPrecipitationSchedule = true;
    }

    public SubsystemWeatherPacket(double fogstart, float ramp, double fogend, float progress) {
        m_fogStartTime = fogstart;
        m_fogRampTime = ramp;
        m_fogEndTime = fogend;
        FogProgress = progress;
        m_hasFogSchedule = true;
    }

    public SubsystemWeatherPacket(double fogstart, float ramp, double fogend) {
        m_fogStartTime = fogstart;
        m_fogRampTime = ramp;
        m_fogEndTime = fogend;
        m_hasFogSchedule = true;
    }

    public override void Serialize(PacketSerializer archive) {
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
    }

    public override void Handle(bool isServer) {
        if (isServer && (From?.GetPlayerData() == null || GameManager.Project.FindSubsystem<SubsystemGameInfo>()?.WorldSettings?.GameMode != GameMode.Creative)) {
            return;
        }
        SubsystemWeather weather = GameManager.Project.FindSubsystem<SubsystemWeather>();
        if (m_weatherType != 0) {
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
            if (isServer) {
                Except = From;
                NetworkManager.Queue(this);
            }
            return;
        }
        if (m_hasPrecipitationSchedule) {
            weather.m_precipitationStartTime = m_precipitationStartTime;
            weather.m_precipitationEndTime = m_precipitationEndTime;
            weather.m_precipitationRampTime = MathUtils.Max(m_precipitationRampTime, 0.001f);
            weather.m_lightningIntensity = m_lightningIntensity;
        }
        if (m_hasFogSchedule) {
            weather.m_fogStartTime = m_fogStartTime;
            weather.m_fogEndTime = MathUtils.Max(m_fogEndTime, m_fogStartTime + 0.001);
            weather.m_fogRampTime = MathUtils.Max(m_fogRampTime, 0.001f);
            weather.FogProgress = MathUtils.Saturate(FogProgress);
        }
    }
}

