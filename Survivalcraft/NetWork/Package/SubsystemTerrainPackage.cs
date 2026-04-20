using System.Collections.Generic;
using Engine;
using Game.NetWork.Serialization;

namespace Game.NetWork.Packages;

public struct CellChange
{
    public int X;
    public int Y;
    public int Z;
    public int Value;
}
public class SubsystemTerrainPackage : IPackage
{
    public byte ID => (byte)PackageType.SubsystemTerrain;

    public enum DataType
    {
        RequestSyncChunks,
        SyncTerrainChunkList,
        ReplyResult,
        ChangeCell,
        RequestChangeCell,
        ChangeCellList          // 有这个的话，后面创世神，命令方块处理方块就不用一个包一个包的发了
    }

    public Client To { get; set; }
    public Client Except { get; set; }
    public Client From { get; set; }
    public ClientState MinNeedState => ClientState.ProjectLoaded;
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

    public SubsystemTerrainPackage()
    {
    }
    public SubsystemTerrainPackage(List<Point2> points)
    {
        m_type = DataType.RequestSyncChunks;
        RelateChunks = [.. points];
    }
    public SubsystemTerrainPackage(List<Point2> resultPoints, byte r)
    {
        m_type = DataType.ReplyResult;
        RelateChunks = [.. resultPoints];
    }

    public SubsystemTerrainPackage(List<TerrainChunk> chunks)
    {
        m_type = DataType.SyncTerrainChunkList;
        Chunks = [.. chunks];
    }

    public SubsystemTerrainPackage(List<CellChange> changeList)
    {
        cellChanges = changeList;
        m_type = DataType.ChangeCellList;
    }

    public SubsystemTerrainPackage(int x, int y, int z, int v, bool request = false)
    {
        m_type = request ? DataType.RequestChangeCell : DataType.ChangeCell;
        m_X = x;
        m_Y = (byte)y;
        m_Z = z;
        m_value = v;
    }
    public void WriteData(PackageStreamWriter writer)
    {
        writer.Write((byte)m_type);
        switch (m_type)
        {
            case DataType.RequestSyncChunks:
                writer.Write(RelateChunks.Count);
                foreach (var p in RelateChunks)
                {
                    writer.Write(p);
                }
                break;
            case DataType.SyncTerrainChunkList:
                writer.Write((ushort)Chunks.Count);
                foreach (var c in Chunks)
                {
                    WriteOneChunk(writer, c);
                }
                break;
            case DataType.RequestChangeCell:
            case DataType.ChangeCell:
                writer.Write(m_X);
                writer.Write(m_Y);
                writer.Write(m_Z);
                writer.Write(m_value);
                break;
            case DataType.ReplyResult:
                writer.Write((ushort)RelateChunks.Count);
                foreach (var p in RelateChunks)
                {
                    writer.Write(p);
                }
                break;
            case DataType.ChangeCellList:
                writer.Write(cellChanges.Count);
                foreach (CellChange cellChange in cellChanges)
                {
                    writer.Write(cellChange.X);
                    writer.Write((byte)cellChange.Y);
                    writer.Write(cellChange.Z);
                    writer.Write(cellChange.Value);
                }
                break;
        }
    }
    public void ReadData(PackageStreamReader reader)
    {
        m_type = (DataType)reader.ReadByte();
        switch (m_type)
        {
            case DataType.RequestSyncChunks:
                RelateChunks = [];
                int count = reader.ReadInt32();
                while (count-- > 0) RelateChunks.Add(reader.ReadPoint2());
                break;
            case DataType.SyncTerrainChunkList:
                ushort cn = reader.ReadUInt16();
                Chunks = new List<TerrainChunk>(cn);
                while (cn-- > 0)
                {
                    ReadChunks(reader);
                }
                break;
            case DataType.RequestChangeCell:
            case DataType.ChangeCell:
                m_X = reader.ReadInt32();
                m_Y = reader.ReadByte();
                m_Z = reader.ReadInt32();
                m_value = reader.ReadInt32();
                break;
            case DataType.ReplyResult:
                RelateChunks = [];
                ushort mc = reader.ReadUInt16();
                while (mc-- > 0)
                {
                    RelateChunks.Add(reader.ReadPoint2());
                }
                break;
            case DataType.ChangeCellList:
                int cellCount = reader.ReadInt32();
                while (cellCount-- > 0)
                {
                    CellChange cellChange = new CellChange();
                    cellChange.X = reader.ReadInt32();
                    cellChange.Y = reader.ReadByte();
                    cellChange.Z = reader.ReadInt32();
                    cellChange.Value = reader.ReadInt32();
                    cellChanges.Add(cellChange);
                }
                break;

        }
    }
    public void WriteOneChunk(PackageStreamWriter writer, TerrainChunk chunk)
    {
        writer.Write(chunk.Coords);
        for (int i = 0; i < chunk.Cells.Length; i++)
        {
            int v = chunk.Cells[i];
            v = AntiXrayObfuscator.ObfuscateValue(v);
            writer.Write(v);
        }
        for (int i = 0; i < chunk.Shafts.Length; i++)
        {
            writer.Write(chunk.Shafts[i]);
        }

        }
        public void ReadChunks(PackageStreamReader reader)
        {
            var p = reader.ReadPoint2();
            TerrainChunk chunk = new TerrainChunk(null, p.X, p.Y);
            for (int i = 0; i < 65536; i++)
            {
                chunk.Cells[i] = reader.ReadInt32();
            }
            for (int i = 0; i < 256; i++)
            {
                chunk.Shafts[i] = reader.ReadInt32();
            }
            Chunks.Add(chunk);
        }
        public void ApplyOneChunk(SubsystemTerrain subsystemTerrain, TerrainChunk chunk)
        {
            int x = chunk.Coords.X;
            int y = chunk.Coords.Y;
            var chunk2 = subsystemTerrain.Terrain.GetChunkAtCoords(x, y);
            if (chunk2 == null) chunk2 = subsystemTerrain.Terrain.AllocateChunk(x, y);
            //如果上面申请没有失败
            if (chunk2 != null)
            {
                chunk2.Cells = chunk.Cells;
                chunk2.Shafts = chunk.Shafts;
                chunk2.ThreadState = TerrainChunkState.InvalidLight;
                chunk2.IsRequested = false;
                chunk2.IsLoaded = true;
                chunk2.WasUpgraded = true;
            }
        }
        public void Handle(ProjectNet projectNet, NetworkBase networkBase, bool isServer)
        {
            if (isServer && m_type != DataType.RequestSyncChunks)
            {
                return;
            }
            var subsystemTerrain = projectNet.FindSubsystem<SubsystemTerrain>();
            switch (m_type)
            {
            case DataType.RequestSyncChunks:
                if (!subsystemTerrain.TerrainUpdater.waitChunkList.TryGetValue(From, out var list))
                {
                    list = [];
                    subsystemTerrain.TerrainUpdater.waitChunkList.Add(From, list);
                }
                list.AddRange(RelateChunks);
                break;
            case DataType.SyncTerrainChunkList:
                foreach (var c in Chunks)
                {
                    ApplyOneChunk(subsystemTerrain, c);
                }
                break;
            case DataType.RequestChangeCell:
            case DataType.ChangeCell:
                {
                    int chunkX = m_X >> 4;
                    int chunkZ = m_Z >> 4;
                    TerrainChunk chunk = subsystemTerrain.Terrain.GetChunkAtCoords(chunkX, chunkZ);
                    if (chunk != null)
                    {
                        if (m_type == DataType.RequestChangeCell)
                        {
                            subsystemTerrain.ChangeCell(m_X, m_Y, m_Z, m_value);
                        }
                        else
                        {
                            subsystemTerrain.ChangeCellNet(m_X, m_Y, m_Z, m_value);
                        }
                    }
                }
                break;
            case DataType.ReplyResult:
                foreach (var p in RelateChunks)
                {
                    var chunk2 = subsystemTerrain.Terrain.GetChunkAtCoords(p.X, p.Y);
                    if (chunk2 != null)
                    {
                        chunk2.IsRequested = false;
                        chunk2.WasUpgraded = true;
                        chunk2.WasDowngraded = true;
                    }
                }
                break;
            case DataType.ChangeCellList:
                foreach (var cellChange in cellChanges)
                {
                    int chunkX = cellChange.X >> 4;
                    int chunkZ = cellChange.Z >> 4;
                    TerrainChunk chunk = subsystemTerrain.Terrain.GetChunkAtCoords(chunkX, chunkZ);
                    if (chunk != null)
                    {
                        subsystemTerrain.ChangeCellNet(cellChange.X, cellChange.Y, cellChange.Z, cellChange.Value);
                    }
                }
                break;
        }
    }
}

