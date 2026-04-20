using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Engine;

namespace Game.NetWork.Packages;

public class SubsystemSeasonPackage : IPackage
{
    public byte ID => (byte)PackageType.SubsystemSeason;
    public Client To { get; set; }
    public Client Except { get; set; }
    public Client From { get; set; }
    public ClientState MinNeedState => ClientState.ProjectLoaded;
    public Season SeasonNet { get; set; }
    public int SeasonIndexNet;
    public float TimeOfSeasonNet { get; set; }
    public SubsystemSeasonPackage()
    {

    }

    public SubsystemSeasonPackage(int seasonIndex, float timeofseason)
    {
        SeasonIndexNet = seasonIndex;//季节编号
        TimeOfSeasonNet = timeofseason;
    }


    public void WriteData(PackageStreamWriter writer)
    {
        writer.Write(SeasonIndexNet);
        writer.Write(TimeOfSeasonNet); // 直接写入 float 类型
    }

    public void ReadData(PackageStreamReader reader)
    {
        SeasonIndexNet = reader.ReadInt32();
        TimeOfSeasonNet = reader.ReadSingle(); // 直接读取 float 类型
    }

    public void Handle(ProjectNet projectNet, NetworkBase networkBase, bool isServer)
    {
        if (isServer)
        {
            return;
        }
        SubsystemSeasons weather = projectNet.FindSubsystem<SubsystemSeasons>();
        weather.Season = (Season)SeasonIndexNet;
        weather.TimeOfSeason = TimeOfSeasonNet;
    }
}
