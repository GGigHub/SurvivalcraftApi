namespace Game.NetWork.Packages;

public class ComponentVitalStatPackage : IPackage {
    public override byte ID => (byte)PackageType.VitalStat;
    public override ClientStage MinNeedState => ClientStage.Playing;

    public enum EventType {
        SyncStat,
        RequestEat
    }

    public int m_inventoryId;
    public int m_slotIndex;
    public int m_foodValue;
    public EventType m_eventType;
    public int m_entityId;
    public float Food = default;
    public float Stamina = default;
    public float Sleep = default;
    public float Wetness = default;
    public float Temperature = default;

    public ComponentVitalStatPackage() {
    }

    public ComponentVitalStatPackage(ComponentVitalStats vitalStats) {
        m_eventType = EventType.SyncStat;
        m_entityId = vitalStats.Entity.Id;
        Food = vitalStats.Food;
        Sleep = vitalStats.Sleep;
        Stamina = vitalStats.Stamina;
        Wetness = vitalStats.Wetness;
        Temperature = vitalStats.Temperature;
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_entityId);
        archive.Enum(ref m_eventType);
        switch (m_eventType) {
            case EventType.SyncStat:
                archive.Value(ref Food);
                archive.Value(ref Sleep);
                archive.Value(ref Wetness);
                archive.Value(ref Temperature);
                archive.Value(ref Stamina);
                break;
        }
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        switch (m_eventType) {
            case EventType.SyncStat:
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
                break;
        }
    }
}
