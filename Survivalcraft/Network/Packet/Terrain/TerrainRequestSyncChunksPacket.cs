using Engine;

namespace Game.Network;

public class TerrainRequestSyncChunksPacket : TerrainPacketBase {
    public override byte ID => (byte)PacketType.TerrainRequestSyncChunks;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;
    public List<Point2> RelateChunks = [];

    public TerrainRequestSyncChunksPacket() { }

    public TerrainRequestSyncChunksPacket(List<Point2> points) {
        RelateChunks = [.. points];
    }

    public override void Serialize(PacketSerializer archive) {
        SerializePoint2List(archive, ref RelateChunks, useUShort: false);
    }

    public override void Handle(bool isServer) {
        SubsystemTerrain subsystemTerrain = GameManager.Project.FindSubsystem<SubsystemTerrain>();

        if (!subsystemTerrain.TerrainUpdater.waitChunkList.TryGetValue(Connection.NetworkGuid, out List<Point2> list)) {
            list = [];
            subsystemTerrain.TerrainUpdater.waitChunkList.Add(Connection.NetworkGuid, list);
        }
        list.AddRange(RelateChunks);
    }
}
