namespace Game.NetWork.Packages;

public class ComponentVitalStatPackage : IPackage {
    public byte ID => (byte)PackageType.VitalStat;
    public Client To { get; set; }
    public Client Except { get; set; }
    public Client From { get; set; }
    public ClientStage MinNeedState => ClientStage.Playing;
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

    /*        public ComponentVitalStatPackage(ComponentVitalStats vitalStats,IInventory inventory, int slotIndex, int value)
            {
                m_eventType = EventType.Eat;
                m_foodValue = value;
                m_inventoryId = inventory.Id;
                m_slotIndex = slotIndex;
                m_entityId = vitalStats.Entity.Id;
            }*/

    public void WriteData(PackageStreamWriter writer) {
        writer.Write(m_entityId);
        writer.WriteEnum(m_eventType);
        switch (m_eventType) {
            case EventType.SyncStat:
                writer.Write(Food);
                writer.Write(Sleep);
                writer.Write(Wetness);
                writer.Write(Temperature);
                writer.Write(Stamina);
                break;
                /*                case EventType.Eat:
                                    writer.Write(m_inventoryId);
                                    writer.Write(m_slotIndex);
                                    writer.Write(m_foodValue);
                                    break;*/
        }
    }
    public void ReadData(PackageStreamReader reader) {
        m_entityId = reader.ReadInt32();
        m_eventType = reader.ReadEnum<EventType>();
        switch (m_eventType) {
            case EventType.SyncStat:
                Food = reader.ReadSingle();
                Sleep = reader.ReadSingle();
                Wetness = reader.ReadSingle();
                Temperature = reader.ReadSingle();
                Stamina = reader.ReadSingle();
                break;
                /*                case EventType.Eat:
                                    m_inventoryId = reader.ReadInt32();
                                    m_slotIndex = reader.ReadInt32();
                                    m_foodValue = reader.ReadInt32();
                                    break;*/
        }
    }

    public void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        switch (m_eventType) {
            case EventType.SyncStat:
                GameManager.Project.FindEntity(m_entityId, (entity) => {
                    var m_vitalStats = entity.FindComponent<ComponentVitalStats>();
                    if (m_vitalStats != null) {
                        m_vitalStats.Food = Food;
                        m_vitalStats.Sleep = Sleep;
                        m_vitalStats.Wetness = Wetness;
                        m_vitalStats.Temperature = Temperature;
                        m_vitalStats.Stamina = Stamina;
                    }
                });
                break;
                /*                case EventType.Eat:
                                    GameManager.Project.FindEntityById(m_entityId, (entity) => {
                                        var m_vitalStats = entity.FindComponent<ComponentVitalStats>();
                                        if (m_vitalStats != null)
                                        {
                                            GameManager.Project.FindSubsystem<SubsystemInventories>().FindInventoryById(m_inventoryId, inventory => {
                                                int v = inventory.GetSlotValue(m_slotIndex);
                                                if (v == m_foodValue)
                                                {
                                                    m_vitalStats.NetEat(inventory, m_slotIndex, m_foodValue);
                                                }
                                            });
                                        }
                                    });
                                    break;*/
        }
    }
}

