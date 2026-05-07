using Engine;

namespace Game.Network;

public class TerrainRequestSyncChunksPacket : Packet {
    public override byte ID => (byte)PacketType.TerrainRequestSyncChunks;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public List<Point2> RelateChunks = [];

    public TerrainRequestSyncChunksPacket() { }

    public TerrainRequestSyncChunksPacket(List<Point2> points) {
        RelateChunks = [.. points];
    }

    public override void Serialize(PacketSerializer archive) {
        TerrainHelpers.SerializePoint2List(archive, ref RelateChunks, useUShort: false);
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

