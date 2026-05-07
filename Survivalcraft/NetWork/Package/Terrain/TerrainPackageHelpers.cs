using Engine;

namespace Game.NetWork;

public static class TerrainPackageHelpers {
    public static void SerializePoint2List(PackageSerializer archive, ref List<Point2> points, bool useUShort) {
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

    public static void SerializeChunkList(PackageSerializer archive, ref List<TerrainChunk> chunks) {
        ushort count = archive.IsWriting ? (ushort)chunks.Count : (ushort)0;
        archive.Value(ref count);
        if (archive.IsReading) {
            chunks = new List<TerrainChunk>(count);
            for (int i = 0; i < count; i++) {
                ReadChunk(archive, chunks);
            }
        }
        else {
            foreach (var chunk in chunks) {
                WriteChunk(archive, chunk);
            }
        }
    }

    public static void SerializeCellChangeList(PackageSerializer archive, ref List<CellChange> cellChanges) {
        int count = archive.IsWriting ? cellChanges.Count : 0;
        archive.Value(ref count);
        if (archive.IsReading) {
            cellChanges = [];
            for (int i = 0; i < count; i++) {
                CellChange cellChange = new();
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

    public static void ApplyOneChunk(SubsystemTerrain subsystemTerrain, TerrainChunk chunk) {
        int x = chunk.Coords.X;
        int y = chunk.Coords.Y;
        TerrainChunk targetChunk = subsystemTerrain.Terrain.GetChunkAtCoords(x, y) ?? subsystemTerrain.Terrain.AllocateChunk(x, y);
        if (targetChunk != null) {
            targetChunk.Cells = chunk.Cells;
            targetChunk.Shafts = chunk.Shafts;
            targetChunk.ThreadState = TerrainChunkState.InvalidLight;
            targetChunk.IsRequested = false;
            targetChunk.IsLoaded = true;
            targetChunk.WasUpgraded = true;
        }
    }

    private static void WriteChunk(PackageSerializer archive, TerrainChunk chunk) {
        Point2 coords = chunk.Coords;
        archive.Value(ref coords);
        for (int i = 0; i < chunk.Cells.Length; i++) {
            int value = chunk.Cells[i];
            archive.Value(ref value);
        }
        for (int i = 0; i < chunk.Shafts.Length; i++) {
            int shaft = chunk.Shafts[i];
            archive.Value(ref shaft);
        }
    }

    private static void ReadChunk(PackageSerializer archive, List<TerrainChunk> chunks) {
        Point2 coords = default;
        archive.Value(ref coords);
        TerrainChunk chunk = new(null, coords.X, coords.Y);
        for (int i = 0; i < 65536; i++) {
            archive.Value(ref chunk.Cells[i]);
        }
        for (int i = 0; i < 256; i++) {
            archive.Value(ref chunk.Shafts[i]);
        }
        chunks.Add(chunk);
    }
}