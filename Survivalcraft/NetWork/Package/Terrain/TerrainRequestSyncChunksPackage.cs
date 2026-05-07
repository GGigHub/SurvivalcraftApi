using Engine;

namespace Game.NetWork;

public class TerrainRequestSyncChunksPackage : IPackage {
    public override byte ID => (byte)PackageType.TerrainRequestSyncChunks;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public List<Point2> RelateChunks = [];

    public TerrainRequestSyncChunksPackage() { }

    public TerrainRequestSyncChunksPackage(List<Point2> points) {
        RelateChunks = [.. points];
    }

    public override void Serialize(PackageSerializer archive) {
        TerrainPackageHelpers.SerializePoint2List(archive, ref RelateChunks, useUShort: false);
    }

    public override void Handle(bool isServer) {
        SubsystemTerrain subsystemTerrain = GameManager.Project.FindSubsystem<SubsystemTerrain>();
        if (!subsystemTerrain.TerrainUpdater.waitChunkList.TryGetValue(From, out var list)) {
            list = [];
            subsystemTerrain.TerrainUpdater.waitChunkList.Add(From, list);
        }
        list.AddRange(RelateChunks);
    }
}