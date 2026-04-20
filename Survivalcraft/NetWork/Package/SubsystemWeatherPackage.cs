using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Engine;

namespace Game.NetWork.Packages;

public class SubsystemWeatherPackage : IPackage
{
    public byte ID => (byte)PackageType.SubsystemWeather;
    public Client To { get; set; }
    public Client Except { get; set; }
    public Client From { get; set; }
    public ClientState MinNeedState => ClientState.ProjectLoaded;
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
    public SubsystemWeatherPackage()
    {
    }

    public SubsystemWeatherPackage(int weatherType)
    {
        m_weatherType = weatherType;
    }
    public SubsystemWeatherPackage(float intensity)
    {
        m_intensity = intensity;
    }
    public SubsystemWeatherPackage(double start, double end, float light)
    {
        m_precipitationEndTime = end;
        m_precipitationStartTime = start;
        m_lightningIntensity = light;
    }

        public SubsystemWeatherPackage(double fogstart, float ramp, double fogend, float progress)
        {
            m_fogStartTime = fogstart;
            m_fogRampTime = ramp;
            m_fogEndTime = fogend;
            FogProgress = progress;
        }
        public SubsystemWeatherPackage(double fogstart, float ramp, double fogend)
        {
            m_fogStartTime = fogstart;
            m_fogRampTime = ramp;
            m_fogEndTime = fogend;
        }
        public void WriteData(PackageStreamWriter writer)
        {
            writer.Write(m_precipitationStartTime);
            writer.Write(m_precipitationEndTime);
            writer.Write(m_lightningIntensity);
            writer.Write(m_weatherType);
            writer.Write(m_fogStartTime);
            writer.Write(m_fogRampTime);
            writer.Write(m_fogEndTime);
        }
        public void ReadData(PackageStreamReader reader)
        {
            m_precipitationStartTime = reader.ReadDouble();
            m_precipitationEndTime = reader.ReadDouble();
            m_lightningIntensity = reader.ReadSingle();
            m_weatherType = reader.ReadInt32();

            m_fogStartTime = reader.ReadDouble();
            m_fogRampTime = reader.ReadSingle();
            m_fogEndTime = reader.ReadDouble();
        }

        public void Handle(ProjectNet projectNet, bool isServer)
        {
            if (isServer && (From.PlayerData == null || NetworkManager.GetEffectiveGameMode(From.PlayerData) != GameMode.Creative))
            {
                return;
            }
            SubsystemWeather weather = projectNet.FindSubsystem<SubsystemWeather>();
            if (m_weatherType != 0)
            {
                switch (m_weatherType)
                {
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
