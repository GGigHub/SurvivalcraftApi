namespace Game.NetWork.Packages;

public class ComponentFurnacePackage : IPackage {
    public byte ID => (byte)PackageType.Furnace;
    public Client To { get; set; }
    public Client Except { get; set; }
    public Client From { get; set; }
    public ClientStage MinNeedState => ClientStage.ProjectLoaded;

    struct FurnacePackageData {
        public float fireTimeRemaining;
        public float smeltingProgress;
        public float heatLevel;
        public int entityID;
    }

    List<FurnacePackageData> furnaceDataList = new List<FurnacePackageData>();

    public ComponentFurnacePackage() { }
    public ComponentFurnacePackage(List<ComponentFurnace> componentFurnaces) {
        foreach (var componentFurnace in componentFurnaces) {
            FurnacePackageData furnaceData = new FurnacePackageData();
            furnaceData.entityID = componentFurnace.Entity.Id;
            furnaceData.fireTimeRemaining = componentFurnace.m_fireTimeRemaining;
            furnaceData.smeltingProgress = componentFurnace.m_smeltingProgress;
            furnaceData.heatLevel = componentFurnace.m_heatLevel;
            furnaceDataList.Add(furnaceData);
        }
    }
    public ComponentFurnacePackage(int entityId, float fireTimeRemaining, float smeltingProgress, float heatLevel) {
        FurnacePackageData furnaceData = new FurnacePackageData();
        furnaceData.entityID = entityId;
        furnaceData.fireTimeRemaining = fireTimeRemaining;
        furnaceData.smeltingProgress = smeltingProgress;
        furnaceData.heatLevel = heatLevel;

        furnaceDataList.Add(furnaceData);
    }
    public void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        foreach (var furnaceData in furnaceDataList) {
            GameManager.Project.FindEntity(furnaceData.entityID, e => {
                var furnace = e.FindComponent<ComponentFurnace>();
                furnace.m_smeltingProgress = furnaceData.smeltingProgress;
                furnace.m_fireTimeRemaining = furnaceData.fireTimeRemaining;
                furnace.m_heatLevel = furnaceData.heatLevel;
            });
        }
    }

    public void ReadData(PackageStreamReader reader) {
        int count = reader.ReadInt32();
        for (int i = 0; i < count; i++) {
            FurnacePackageData furnaceData = new FurnacePackageData();
            furnaceData.entityID = reader.ReadInt32();
            furnaceData.fireTimeRemaining = reader.ReadSingle();
            furnaceData.smeltingProgress = reader.ReadSingle();
            furnaceData.heatLevel = reader.ReadSingle();
            furnaceDataList.Add(furnaceData);
        }
    }

    public void WriteData(PackageStreamWriter writer) {
        writer.Write(furnaceDataList.Count);
        foreach (var furnace in furnaceDataList) {
            writer.Write(furnace.entityID);
            writer.Write(furnace.fireTimeRemaining);
            writer.Write(furnace.smeltingProgress);
            writer.Write(furnace.heatLevel);
        }

    }
}
