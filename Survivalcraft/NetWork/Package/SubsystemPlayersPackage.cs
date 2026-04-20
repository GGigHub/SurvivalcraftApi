using Game.NetWork.Packages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Game.NetWork.Packages;

public class SubsystemPlayersPackage : IPackage
{
    public byte ID => (byte)PackageType.SubsystemPlayers;
    public Client To { get; set; }
    public Client Except { get; set; }
    public Client From { get; set; }

    public ClientState MinNeedState => ClientState.Playing;

    public List<ComponentPlayerPackage> componentPlayerPackageList = new List<ComponentPlayerPackage>();

    public void AddPackage(ComponentPlayerPackage componentPlayerPackage)
    {
        lock(componentPlayerPackageList)
        {
            componentPlayerPackageList.Add(componentPlayerPackage);
        }
    }

    public void ReadData(PackageStreamReader reader)
    {
        componentPlayerPackageList.Clear();
        int count = reader.ReadInt32();
        for(int index = 0; index < count; index++)
        {
            ComponentPlayerPackage componentPlayerPackage = new ComponentPlayerPackage();
            componentPlayerPackage.ReadData(reader);
            componentPlayerPackage.NeedHandleMainPlayer = false;
            componentPlayerPackageList.Add(componentPlayerPackage);
        }
    }

    public void WriteData(PackageStreamWriter writer)
    {
        lock(componentPlayerPackageList)
        {
            writer.Write(componentPlayerPackageList.Count);
            foreach (var package in componentPlayerPackageList)
            {
                package.WriteData(writer);
            }
        }
    }
    public void Handle(ProjectNet projectNet, NetworkBase networkBase, bool isServer)
    {
        foreach (var package in componentPlayerPackageList)
        {
            package.Handle(projectNet, networkBase, isServer);
        }
    }
}
