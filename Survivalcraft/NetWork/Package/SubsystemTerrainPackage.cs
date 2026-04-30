using Engine;

namespace Game.NetWork.Packages;

public struct CellChange {
    public int X;
    public int Y;
    public int Z;
    public int Value;
}

internal static class TerrainPackageHelpers {
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

public class TerrainReplyResultPackage : IPackage {
    public override byte ID => (byte)PackageType.TerrainReplyResult;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public List<Point2> RelateChunks = [];

    public TerrainReplyResultPackage() { }

    public TerrainReplyResultPackage(List<Point2> resultPoints) {
        RelateChunks = [.. resultPoints];
    }

    public override void Serialize(PackageSerializer archive) {
        TerrainPackageHelpers.SerializePoint2List(archive, ref RelateChunks, useUShort: true);
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        SubsystemTerrain subsystemTerrain = GameManager.Project.FindSubsystem<SubsystemTerrain>();
        foreach (Point2 point in RelateChunks) {
            TerrainChunk chunk = subsystemTerrain.Terrain.GetChunkAtCoords(point.X, point.Y);
            if (chunk != null) {
                chunk.IsRequested = false;
                chunk.WasUpgraded = true;
                chunk.WasDowngraded = true;
            }
        }
    }
}

public class TerrainChangeCellPackage : IPackage {
    public override byte ID => (byte)PackageType.TerrainChangeCell;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public int m_X;
    public byte m_Y;
    public int m_Z;
    public int m_value;

    public TerrainChangeCellPackage() { }

    public TerrainChangeCellPackage(int x, int y, int z, int value) {
        m_X = x;
        m_Y = (byte)y;
        m_Z = z;
        m_value = value;
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_X);
        archive.Value(ref m_Y);
        archive.Value(ref m_Z);
        archive.Value(ref m_value);
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        SubsystemTerrain subsystemTerrain = GameManager.Project.FindSubsystem<SubsystemTerrain>();
        int chunkX = m_X >> 4;
        int chunkZ = m_Z >> 4;
        TerrainChunk chunk = subsystemTerrain.Terrain.GetChunkAtCoords(chunkX, chunkZ);
        if (chunk != null) {
            subsystemTerrain.ChangeCellNet(m_X, m_Y, m_Z, m_value);
        }
    }
}

public class TerrainRequestChangeCellPackage : IPackage {
    public override byte ID => (byte)PackageType.TerrainRequestChangeCell;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public int m_X;
    public byte m_Y;
    public int m_Z;
    public int m_value;

    public TerrainRequestChangeCellPackage() { }

    public TerrainRequestChangeCellPackage(int x, int y, int z, int value) {
        m_X = x;
        m_Y = (byte)y;
        m_Z = z;
        m_value = value;
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_X);
        archive.Value(ref m_Y);
        archive.Value(ref m_Z);
        archive.Value(ref m_value);
    }

    public override void Handle(bool isServer) {
        SubsystemTerrain subsystemTerrain = GameManager.Project.FindSubsystem<SubsystemTerrain>();
        int chunkX = m_X >> 4;
        int chunkZ = m_Z >> 4;
        TerrainChunk chunk = subsystemTerrain.Terrain.GetChunkAtCoords(chunkX, chunkZ);
        if (chunk != null) {
            subsystemTerrain.ChangeCell(m_X, m_Y, m_Z, m_value);
        }
    }
}

public class TerrainChangeCellListPackage : IPackage {
    public override byte ID => (byte)PackageType.TerrainChangeCellList;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public List<CellChange> cellChanges = [];

    public TerrainChangeCellListPackage() { }

    public TerrainChangeCellListPackage(List<CellChange> changeList) {
        cellChanges = changeList;
    }

    public override void Serialize(PackageSerializer archive) {
        TerrainPackageHelpers.SerializeCellChangeList(archive, ref cellChanges);
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
