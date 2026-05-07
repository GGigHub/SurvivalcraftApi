using Engine;

namespace Game.NetWork;

public class TerrainChangeCellListPackage : IPackage {
    public override byte ID => (byte)PackageType.TerrainChangeCellList;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public List<CellChange> cellChanges = [];

    public TerrainChangeCellListPackage() { }

    public TerrainChangeCellListPackage(List<CellChange> changeList) {
        cellChanges = changeList;
    }

    public override void Serialize(PackageSerializer archive) {
        TerrainPackageHelpers.SerializeCellChangeList(archive, ref cellChanges);
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        SubsystemTerrain subsystemTerrain = GameManager.Project.FindSubsystem<SubsystemTerrain>();
        foreach (CellChange cellChange in cellChanges) {
            int chunkX = cellChange.X >> 4;
            int chunkZ = cellChange.Z >> 4;
            TerrainChunk chunk = subsystemTerrain.Terrain.GetChunkAtCoords(chunkX, chunkZ);
            if (chunk != null) {
                subsystemTerrain.ChangeCellNet(cellChange.X, cellChange.Y, cellChange.Z, cellChange.Value);
            }
        }
    }
}