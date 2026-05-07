

namespace Game.NetWork;

public class ComponentFurnacePackage : IPackage {
    public override byte ID => (byte)PackageType.Furnace;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;

    public struct FurnacePackageData {
        public float fireTimeRemaining;
        public float smeltingProgress;
        public float heatLevel;
        public int entityID;
    }

    private readonly List<FurnacePackageData> furnaceDataList = [];

    public ComponentFurnacePackage() {
    }

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

    public override void Serialize(PackageSerializer archive) {
        int count = furnaceDataList.Count;
        archive.Value(ref count);
        if (archive.IsReading) {
            furnaceDataList.Clear();
            for (int i = 0; i < count; i++) {
                FurnacePackageData furnaceData = new FurnacePackageData();
                archive.Value(ref furnaceData.entityID);
                archive.Value(ref furnaceData.fireTimeRemaining);
                archive.Value(ref furnaceData.smeltingProgress);
                archive.Value(ref furnaceData.heatLevel);
                furnaceDataList.Add(furnaceData);
            }
        }
        else {
            for (int i = 0; i < count; i++) {
                FurnacePackageData furnaceData = furnaceDataList[i];
                archive.Value(ref furnaceData.entityID);
                archive.Value(ref furnaceData.fireTimeRemaining);
                archive.Value(ref furnaceData.smeltingProgress);
                archive.Value(ref furnaceData.heatLevel);
            }
        }
    }

    public override void Handle(bool isServer) {
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
}