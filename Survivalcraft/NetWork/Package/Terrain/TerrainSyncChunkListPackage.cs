using Engine;

namespace Game.NetWork;

public class TerrainSyncChunkListPackage : IPackage {
    public override byte ID => (byte)PackageType.TerrainSyncChunkList;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public List<TerrainChunk> Chunks = [];

    public TerrainSyncChunkListPackage() { }

    public TerrainSyncChunkListPackage(List<TerrainChunk> chunks) {
        Chunks = [.. chunks];
    }

    public override void Serialize(PackageSerializer archive) {
        TerrainPackageHelpers.SerializeChunkList(archive, ref Chunks);
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        SubsystemTerrain subsystemTerrain = GameManager.Project.FindSubsystem<SubsystemTerrain>();
        foreach (TerrainChunk chunk in Chunks) {
            TerrainPackageHelpers.ApplyOneChunk(subsystemTerrain, chunk);
        }
    }
}