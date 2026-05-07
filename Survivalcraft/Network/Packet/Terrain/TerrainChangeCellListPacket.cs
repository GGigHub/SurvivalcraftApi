using Engine;

namespace Game.Network;

public class TerrainChangeCellListPacket : Packet {
    public override byte ID => (byte)PacketType.TerrainChangeCellList;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public List<CellChange> cellChanges = [];

    public TerrainChangeCellListPacket() { }

    public TerrainChangeCellListPacket(List<CellChange> changeList) {
        cellChanges = changeList;
    }

    public override void Serialize(PacketSerializer archive) {
        TerrainHelpers.SerializeCellChangeList(archive, ref cellChanges);
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

