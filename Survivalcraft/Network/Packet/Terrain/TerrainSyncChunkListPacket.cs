using Engine;

namespace Game.Network;

public class TerrainSyncChunkListPacket : TerrainPacketBase {
    public override byte ID => (byte)PacketType.TerrainSyncChunkList;
    public override ClientState MinNeedState => ClientState.ProjectLoaded;
    public List<TerrainChunk> Chunks = [];

    public TerrainSyncChunkListPacket() { }

    public TerrainSyncChunkListPacket(List<TerrainChunk> chunks) {
        Chunks = [.. chunks];
    }

    public override void Serialize(PacketSerializer archive) {
        SerializeChunkList(archive, ref Chunks);
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        SubsystemTerrain subsystemTerrain = GameManager.Project.FindSubsystem<SubsystemTerrain>();
        foreach (TerrainChunk chunk in Chunks) {
            ApplyOneChunk(subsystemTerrain, chunk);
        }
    }
}

