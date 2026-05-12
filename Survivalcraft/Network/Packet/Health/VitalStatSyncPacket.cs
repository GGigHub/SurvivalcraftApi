

namespace Game.Network;

public class VitalStatSyncPacket : Packet {
    public override byte ID => (byte)PacketType.VitalStatSync;
    public override ClientState MinNeedState => ClientState.Playing;
    public int m_entityId;
    public float Food = default;
    public float Stamina = default;
    public float Sleep = default;
    public float Wetness = default;
    public float Temperature = default;

    public VitalStatSyncPacket() { }

    public VitalStatSyncPacket(ComponentVitalStats vitalStats) {
        m_entityId = vitalStats.Entity.Id;
        Food = vitalStats.Food;
        Sleep = vitalStats.Sleep;
        Stamina = vitalStats.Stamina;
        Wetness = vitalStats.Wetness;
        Temperature = vitalStats.Temperature;
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_entityId);
        archive.Value(ref Food);
        archive.Value(ref Sleep);
        archive.Value(ref Wetness);
        archive.Value(ref Temperature);
        archive.Value(ref Stamina);
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        GameManager.Project.FindEntity(m_entityId, entity => {
            var vitalStats = entity.FindComponent<ComponentVitalStats>();
            if (vitalStats != null) {
                vitalStats.Food = Food;
                vitalStats.Sleep = Sleep;
                vitalStats.Wetness = Wetness;
                vitalStats.Temperature = Temperature;
                vitalStats.Stamina = Stamina;
            }
        });
    }
}

