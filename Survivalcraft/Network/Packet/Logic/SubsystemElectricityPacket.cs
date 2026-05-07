using Engine;

namespace Game.Network;

public class SubsystemElectricityPacket : Packet {
    public override byte ID => (byte)PacketType.Electricity;
    public override ClientStage MinNeedState => ClientStage.Playing;
    public SubsystemElectricity m_subsystem;
    public List<SubsystemElectricity.NetSimulate> NetSimulates = new List<SubsystemElectricity.NetSimulate>();

    public SubsystemElectricityPacket() {
    }

    public SubsystemElectricityPacket(List<SubsystemElectricity.NetSimulate> netSimulates) {
        NetSimulates.AddRange(netSimulates);
    }

    public override void Serialize(PacketSerializer archive) {
        byte count = archive.IsWriting ? (byte)NetSimulates.Count : (byte)0;
        archive.Value(ref count);
        if (archive.IsReading) {
            NetSimulates.Clear();
            for (byte i = 0; i < count; i++) {
                SubsystemElectricity.NetSimulate netSimulate = new SubsystemElectricity.NetSimulate();
                archive.Value(ref netSimulate.StartStep);
                ushort electricsCount = 0;
                archive.Value(ref electricsCount);
                for (int p = 0; p < electricsCount; p++) {
                    Point3 point = default;
                    float value = 0f;
                    archive.Value(ref point);
                    archive.Value(ref value);
                    netSimulate.SaveData.Add(point, value);
                }
                NetSimulates.Add(netSimulate);
            }
        }
        else {
            foreach (var netSimulate in NetSimulates) {
                int startStep = netSimulate.StartStep;
                archive.Value(ref startStep);
                ushort electricsCount = (ushort)netSimulate.SaveData.Count;
                archive.Value(ref electricsCount);
                foreach (var item in netSimulate.SaveData) {
                    Point3 point = item.Key;
                    float value = item.Value;
                    archive.Value(ref point);
                    archive.Value(ref value);
                }
            }
        }
    }

    public override void Handle(bool isServer) {
        m_subsystem = GameManager.Project.FindSubsystem<SubsystemElectricity>();
        m_subsystem.list.AddRange(NetSimulates);
    }
}

