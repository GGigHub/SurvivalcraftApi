using Engine;

namespace Game.Network;

public struct CellChange {
    public int X;
    public int Y;
    public int Z;
    public int Value;
    public long Revision;
}


public class TerrainChangeCellListPacket : TerrainPacketBase {
    public override byte ID => (byte)PacketType.TerrainChangeCellList;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;
    public List<CellChange> cellChanges = [];

    public TerrainChangeCellListPacket() { }

    public TerrainChangeCellListPacket(List<CellChange> changeList) {
        cellChanges = changeList;
    }

    public override void Serialize(PacketSerializer archive) {
        SerializeCellChangeList(archive, ref cellChanges);
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        SubsystemTerrain subsystemTerrain = GameManager.Project.FindSubsystem<SubsystemTerrain>();
        foreach (CellChange cellChange in cellChanges) {
            ApplyRevisionedCellChange(
                subsystemTerrain,
                cellChange.X,
                cellChange.Y,
                cellChange.Z,
                cellChange.Value,
                cellChange.Revision);
        }
    }
}

