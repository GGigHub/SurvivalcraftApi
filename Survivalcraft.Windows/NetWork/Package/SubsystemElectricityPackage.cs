using System.Collections.Generic;
using System.Linq;

namespace Game.NetWork.Packages;

public class SubsystemElectricityPackage : IPackage
{
    public byte ID => (byte)PackageType.SubsystemElectricity;
    public Client To { get; set; }
    public Client Except { get; set; }
    public Client From { get; set; }
    public SubsystemElectricity m_subsystem;
    public List<SubsystemElectricity.NetSimulate> NetSimulates = new List<SubsystemElectricity.NetSimulate>();
    public ClientState MinNeedState => ClientState.Playing;
    public SubsystemElectricityPackage()
    {
    }
    public SubsystemElectricityPackage(List<SubsystemElectricity.NetSimulate> netSimulates)
    {
        NetSimulates.AddRange(netSimulates);
    }
    public void WriteData(PackageStreamWriter writer)
    {
        writer.Write((byte)NetSimulates.Count);
        for (int p = 0; p < NetSimulates.Count; p++)
        {
            var NetSimulate = NetSimulates[p];
            writer.Write(NetSimulate.StartStep);
            writer.Write((ushort)NetSimulate.SaveData.Count);
            foreach (var item in NetSimulate.SaveData)
            {
                writer.WriteBlockPoint(item.Key);
                writer.Write(item.Value);
            }
        }
    }
    public void ReadData(PackageStreamReader reader)
    {
        byte count = reader.ReadByte();
        for (byte i = 0; i < count; i++)
        {
            SubsystemElectricity.NetSimulate netSimulate = new SubsystemElectricity.NetSimulate();
            netSimulate.StartStep = reader.ReadInt32();
            ushort m_electricsCount = reader.ReadUInt16();
            for (int p = 0; p < m_electricsCount; p++)
            {
                netSimulate.SaveData.Add(reader.ReadBlockPoint(), reader.ReadSingle());
            }
            NetSimulates.Add(netSimulate);
        }
    }

    public void Handle(ProjectNet projectNet, NetNode netNode, bool isServer)
    {
        m_subsystem = projectNet.FindSubsystem<SubsystemElectricity>();
        m_subsystem.list.AddRange(NetSimulates);
    }
}
