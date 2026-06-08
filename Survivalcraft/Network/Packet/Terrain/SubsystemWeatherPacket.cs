

namespace Game.Network;

public class SubsystemWeatherPacket : Packet {
    public override byte ID => (byte)PacketType.Weather;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;
    public double m_precipitationStartTime;
    public double m_precipitationEndTime;
    public float m_lightningIntensity;
    public int m_weatherType = 0;
    public int FogType = 0;
    public double m_fogStartTime;
    public double m_fogEndTime;
    public float m_fogRampTime;
    public float FogProgress;
    public float m_intensity;

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
    }

    public SubsystemWeatherPacket(double fogstart, float ramp, double fogend, float progress) {
        m_fogStartTime = fogstart;
        m_fogRampTime = ramp;
        m_fogEndTime = fogend;
        FogProgress = progress;
    }

    public SubsystemWeatherPacket(double fogstart, float ramp, double fogend) {
        m_fogStartTime = fogstart;
        m_fogRampTime = ramp;
        m_fogEndTime = fogend;
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_precipitationStartTime);
        archive.Value(ref m_precipitationEndTime);
        archive.Value(ref m_lightningIntensity);
        archive.Value(ref m_weatherType);
        archive.Value(ref m_fogStartTime);
        archive.Value(ref m_fogRampTime);
        archive.Value(ref m_fogEndTime);
    }

    public override void Handle(bool isServer) {
        if (isServer && (Connection?.GetPlayerData() == null || GameManager.Project.FindSubsystem<SubsystemGameInfo>()?.WorldSettings?.GameMode != GameMode.Creative)) {
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
            return;
        }
        weather.m_precipitationStartTime = m_precipitationStartTime;
        weather.m_precipitationEndTime = m_precipitationEndTime;
        weather.m_lightningIntensity = m_lightningIntensity;
        weather.m_fogStartTime = m_fogStartTime;
        weather.m_fogEndTime = m_fogEndTime;
        weather.m_fogRampTime = m_fogRampTime;
    }
}

