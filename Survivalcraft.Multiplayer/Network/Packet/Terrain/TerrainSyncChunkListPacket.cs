using Engine;

namespace Game.Network;

public class TerrainSyncChunkListPacket : TerrainPacketBase {
    public override byte ID => (byte)PacketType.TerrainSyncChunkList;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;
    public List<TerrainChunk> Chunks = [];
    public List<TerrainChunkSyncData> SyncData = [];

    public TerrainSyncChunkListPacket() { }

    public TerrainSyncChunkListPacket(List<TerrainChunk> chunks) {
        Chunks = [.. chunks];
    }

    internal TerrainSyncChunkListPacket(IEnumerable<TerrainChunkSyncData> syncData) {
        SyncData = [.. syncData];
    }

    public override void Serialize(PacketSerializer archive) {
        SerializeChunkSyncDataList(archive, ref Chunks, ref SyncData);
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        SubsystemTerrain subsystemTerrain = GameManager.Project.FindSubsystem<SubsystemTerrain>();
        foreach (TerrainChunkSyncData data in SyncData) {
            ApplyOneChunkSyncData(subsystemTerrain, data);
        }
    }
}
