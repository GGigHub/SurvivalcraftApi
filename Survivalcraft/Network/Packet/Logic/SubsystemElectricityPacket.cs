using Engine;
using GameEntitySystem;

namespace Game.Network;

public class SubsystemElectricityPacket : Packet {
    public const int MaximumVoltageEntries = 4096;

    public override byte ID => (byte)PacketType.Electricity;
    public override NetworkState MinNeedState => NetworkState.Playing;
    public SubsystemElectricity m_subsystem;
    public List<SubsystemElectricity.NetSimulate> NetSimulates = new List<SubsystemElectricity.NetSimulate>();
    public long m_revision;

    public SubsystemElectricityPacket() {
    }

    public SubsystemElectricityPacket(List<SubsystemElectricity.NetSimulate> netSimulates) {
        NetSimulates.AddRange(netSimulates);
        if (GameManager.Project != null) {
            m_revision = NetworkLogicStateTracker.AdvanceAuthoritativeRevision(
                GameManager.Project,
                NetworkLogicStateKey.Electricity());
        }
    }

    public SubsystemElectricityPacket(
        SubsystemElectricity subsystem,
        long revision) {
        ArgumentNullException.ThrowIfNull(subsystem);
        SubsystemElectricity.NetSimulate snapshot = new() {
            StartStep = subsystem.CircuitStep
        };
        foreach ((Point3 point, float voltage) in subsystem.m_persistentElementsVoltages) {
            snapshot.SaveData[point] = voltage;
        }
        NetSimulates.Add(snapshot);
        m_revision = revision;
    }

    public override void Serialize(PacketSerializer archive) {
        int count = archive.IsWriting ? NetSimulates.Count : 0;
        archive.Value(ref count);
        archive.Value(ref m_revision);
        if (archive.IsReading) {
            if (count is < 0 or > 64) {
                throw new InvalidDataException(
                    $"Electricity packet contains invalid snapshot count: {count}.");
            }
            NetSimulates.Clear();
            int totalEntries = 0;
            for (int i = 0; i < count; i++) {
                SubsystemElectricity.NetSimulate netSimulate = new SubsystemElectricity.NetSimulate();
                archive.Value(ref netSimulate.StartStep);
                int electricsCount = 0;
                archive.Value(ref electricsCount);
                if (electricsCount < 0
                    || checked(totalEntries + electricsCount) > MaximumVoltageEntries) {
                    throw new InvalidDataException(
                        $"Electricity packet contains invalid voltage count: {electricsCount}.");
                }
                totalEntries += electricsCount;
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
                int electricsCount = netSimulate.SaveData.Count;
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
        if (isServer
            || m_revision < 0
            || NetSimulates.Count == 0
            || NetSimulates.Sum(item => item.SaveData.Count) > MaximumVoltageEntries
            || NetSimulates.Any(item => item.SaveData.Values.Any(value => !float.IsFinite(value)))) {
            return;
        }
        Project project = GameManager.Project;
        if (project == null
            || !NetworkLogicStateTracker.Receive(
                project,
                NetworkLogicStateKey.Electricity(),
                m_revision).Apply) {
            return;
        }
        m_subsystem = GameManager.Project.FindSubsystem<SubsystemElectricity>();
        SubsystemElectricity.NetSimulate latest = NetSimulates
            .OrderBy(item => item.StartStep)
            .Last();
        m_subsystem.m_persistentElementsVoltages.Clear();
        foreach ((Point3 point, float voltage) in latest.SaveData) {
            m_subsystem.m_persistentElementsVoltages[point] = voltage;
        }
        m_subsystem.list.Clear();
        m_subsystem.list.AddRange(NetSimulates.OrderBy(item => item.StartStep));
    }
}

