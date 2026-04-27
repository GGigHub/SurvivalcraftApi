using Engine;

namespace Game.NetWork.Packages;

public struct CellChange {
    public int X;
    public int Y;
    public int Z;
    public int Value;
}

public class SubsystemTerrainPackage : IPackage {
    public override byte ID => (byte)PackageType.Terrain;

    public enum DataType {
        RequestSyncChunks,
        SyncTerrainChunkList,
        ReplyResult,
        ChangeCell,
        RequestChangeCell,
        ChangeCellList
    }

    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public DataType m_type;
    public List<Point2> RelateChunks;
    public List<TerrainChunk> Chunks;
    public int[] Cells;
    public int[] Shafts;

    public int m_X;
    public byte m_Y;
    public int m_Z;
    public int m_value;

    public List<Point3> m_modifyCells = [];
    public List<int> m_modifyValues = [];
    public List<CellChange> cellChanges = [];

    public byte m_result;

    public SubsystemTerrainPackage() {
    }

    public SubsystemTerrainPackage(List<Point2> points) {
        m_type = DataType.RequestSyncChunks;
        RelateChunks = [.. points];
    }

    public SubsystemTerrainPackage(List<Point2> resultPoints, byte r) {
        m_type = DataType.ReplyResult;
        RelateChunks = [.. resultPoints];
    }

    public SubsystemTerrainPackage(List<TerrainChunk> chunks) {
        m_type = DataType.SyncTerrainChunkList;
        Chunks = [.. chunks];
    }

    public SubsystemTerrainPackage(List<CellChange> changeList) {
        cellChanges = changeList;
        m_type = DataType.ChangeCellList;
    }

    public SubsystemTerrainPackage(int x, int y, int z, int v, bool request = false) {
        m_type = request ? DataType.RequestChangeCell : DataType.ChangeCell;
        m_X = x;
        m_Y = (byte)y;
        m_Z = z;
        m_value = v;
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Enum(ref m_type);
        switch (m_type) {
            case DataType.RequestSyncChunks:
                SerializePoint2List(archive, ref RelateChunks, useUShort: false);
                break;
            case DataType.SyncTerrainChunkList:
                SerializeChunkList(archive);
                break;
            case DataType.RequestChangeCell:
            case DataType.ChangeCell:
                archive.Value(ref m_X);
                archive.Value(ref m_Y);
                archive.Value(ref m_Z);
                archive.Value(ref m_value);
                break;
            case DataType.ReplyResult:
                SerializePoint2List(archive, ref RelateChunks, useUShort: true);
                break;
            case DataType.ChangeCellList:
                SerializeCellChangeList(archive);
                break;
        }
    }

    private void SerializePoint2List(PackageSerializer archive, ref List<Point2> points, bool useUShort) {
        int count = archive.IsWriting ? points.Count : 0;
        if (useUShort) {
            ushort shortCount = (ushort)count;
            archive.Value(ref shortCount);
            count = shortCount;
        }
        else {
            archive.Value(ref count);
        }
        if (archive.IsReading) {
            points = [];
            for (int i = 0; i < count; i++) {
                Point2 point = default;
                archive.Value(ref point);
                points.Add(point);
            }
        }
        else {
            for (int i = 0; i < count; i++) {
                Point2 point = points[i];
                archive.Value(ref point);
            }
        }
    }

    private void SerializeChunkList(PackageSerializer archive) {
        ushort count = archive.IsWriting ? (ushort)Chunks.Count : (ushort)0;
        archive.Value(ref count);
        if (archive.IsReading) {
            Chunks = new List<TerrainChunk>(count);
            for (int i = 0; i < count; i++) {
                ReadChunk(archive);
            }
        }
        else {
            foreach (var chunk in Chunks) {
                WriteChunk(archive, chunk);
            }
        }
    }

    private void WriteChunk(PackageSerializer archive, TerrainChunk chunk) {
        Point2 coords = chunk.Coords;
        archive.Value(ref coords);
        for (int i = 0; i < chunk.Cells.Length; i++) {
            int v = chunk.Cells[i];
            archive.Value(ref v);
        }
        for (int i = 0; i < chunk.Shafts.Length; i++) {
            int shaft = chunk.Shafts[i];
            archive.Value(ref shaft);
        }
    }

    private void ReadChunk(PackageSerializer archive) {
        Point2 p = default;
        archive.Value(ref p);
        TerrainChunk chunk = new TerrainChunk(null, p.X, p.Y);
        for (int i = 0; i < 65536; i++) {
            archive.Value(ref chunk.Cells[i]);
        }
        for (int i = 0; i < 256; i++) {
            archive.Value(ref chunk.Shafts[i]);
        }
        Chunks.Add(chunk);
    }

    private void SerializeCellChangeList(PackageSerializer archive) {
        int count = archive.IsWriting ? cellChanges.Count : 0;
        archive.Value(ref count);
        if (archive.IsReading) {
            cellChanges = [];
            for (int i = 0; i < count; i++) {
                CellChange cellChange = new CellChange();
                archive.Value(ref cellChange.X);
                archive.Value(ref cellChange.Y);
                archive.Value(ref cellChange.Z);
                archive.Value(ref cellChange.Value);
                cellChanges.Add(cellChange);
            }
        }
        else {
            for (int i = 0; i < count; i++) {
                CellChange cellChange = cellChanges[i];
                archive.Value(ref cellChange.X);
                archive.Value(ref cellChange.Y);
                archive.Value(ref cellChange.Z);
                archive.Value(ref cellChange.Value);
            }
        }
    }

    public void ApplyOneChunk(SubsystemTerrain subsystemTerrain, TerrainChunk chunk) {
        int x = chunk.Coords.X;
        int y = chunk.Coords.Y;
        var chunk2 = subsystemTerrain.Terrain.GetChunkAtCoords(x, y);
        if (chunk2 == null) chunk2 = subsystemTerrain.Terrain.AllocateChunk(x, y);
        if (chunk2 != null) {
            chunk2.Cells = chunk.Cells;
            chunk2.Shafts = chunk.Shafts;
            chunk2.ThreadState = TerrainChunkState.InvalidLight;
            chunk2.IsRequested = false;
            chunk2.IsLoaded = true;
            chunk2.WasUpgraded = true;
        }
    }

    public override void Handle(bool isServer) {
        if (isServer && m_type != DataType.RequestSyncChunks) {
            return;
        }
        var subsystemTerrain = GameManager.Project.FindSubsystem<SubsystemTerrain>();
        switch (m_type) {
            case DataType.RequestSyncChunks:
                if (!subsystemTerrain.TerrainUpdater.waitChunkList.TryGetValue(From, out var list)) {
                    list = [];
                    subsystemTerrain.TerrainUpdater.waitChunkList.Add(From, list);
                }
                list.AddRange(RelateChunks);
                break;
            case DataType.SyncTerrainChunkList:
                foreach (var c in Chunks) {
                    ApplyOneChunk(subsystemTerrain, c);
                }
                break;
            case DataType.RequestChangeCell:
            case DataType.ChangeCell:
                {
                    int chunkX = m_X >> 4;
                    int chunkZ = m_Z >> 4;
                    TerrainChunk chunk = subsystemTerrain.Terrain.GetChunkAtCoords(chunkX, chunkZ);
                    if (chunk != null) {
                        if (m_type == DataType.RequestChangeCell) subsystemTerrain.ChangeCell(m_X, m_Y, m_Z, m_value);
                        else subsystemTerrain.ChangeCellNet(m_X, m_Y, m_Z, m_value);
                    }
                }
                break;
            case DataType.ReplyResult:
                foreach (var p in RelateChunks) {
                    var chunk2 = subsystemTerrain.Terrain.GetChunkAtCoords(p.X, p.Y);
                    if (chunk2 != null) {
                        chunk2.IsRequested = false;
                        chunk2.WasUpgraded = true;
                        chunk2.WasDowngraded = true;
                    }
                }
                break;
            case DataType.ChangeCellList:
                foreach (var cellChange in cellChanges) {
                    int chunkX = cellChange.X >> 4;
                    int chunkZ = cellChange.Z >> 4;
                    TerrainChunk chunk = subsystemTerrain.Terrain.GetChunkAtCoords(chunkX, chunkZ);
                    if (chunk != null) {
                        subsystemTerrain.ChangeCellNet(cellChange.X, cellChange.Y, cellChange.Z, cellChange.Value);
                    }
                }
                break;
        }
    }
}
