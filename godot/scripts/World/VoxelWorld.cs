using System;
using System.Collections.Generic;

namespace SurvivalcraftGodot;

/// <summary>
/// 区块字典、改方块、阳光。组网格和渲染不在这里。
/// </summary>
public sealed class VoxelWorld
{
    readonly Dictionary<ChunkPos, Chunk> _chunks = new();
    readonly Queue<FlowCell> _flow = new();
    readonly Queue<LightCell> _blockAdd = new();
    readonly Queue<LightCell> _blockRemove = new();
    readonly Queue<LightCell> _sunAdd = new();
    readonly Queue<LightCell> _sunRemove = new();

    public IEnumerable<ChunkPos> Chunks => _chunks.Keys;

    public bool TryGetChunk(ChunkPos pos, out Chunk chunk) => _chunks.TryGetValue(pos, out chunk!);

    public Chunk GetOrCreate(ChunkPos pos)
    {
        if (_chunks.TryGetValue(pos, out Chunk? existing))
        {
            return existing;
        }

        Chunk created = new();
        _chunks.Add(pos, created);
        return created;
    }

    public bool Remove(ChunkPos pos) => _chunks.Remove(pos);

    public ushort GetBlock(int x, int y, int z)
    {
        if (y < 0 || y >= Chunk.Height)
        {
            return BlockIds.Boundary;
        }

        ChunkPos pos = CoordOf(x, z);
        if (!_chunks.TryGetValue(pos, out Chunk? chunk))
        {
            return BlockIds.Boundary;
        }

        return chunk.Blocks[Chunk.Index(Chunk.Mod(x, Chunk.Size), y, Chunk.Mod(z, Chunk.Size))];
    }

    public byte GetState(int x, int y, int z)
    {
        if (y < 0 || y >= Chunk.Height)
        {
            return 0;
        }

        ChunkPos pos = CoordOf(x, z);
        if (!_chunks.TryGetValue(pos, out Chunk? chunk))
        {
            return 0;
        }

        return chunk.State[Chunk.Index(Chunk.Mod(x, Chunk.Size), y, Chunk.Mod(z, Chunk.Size))];
    }

    public byte GetSky(int x, int y, int z)
    {
        if (y < 0 || y >= Chunk.Height)
        {
            return 0;
        }

        ChunkPos pos = CoordOf(x, z);
        if (!_chunks.TryGetValue(pos, out Chunk? chunk))
        {
            return 0;
        }

        return chunk.Sky[Chunk.Index(Chunk.Mod(x, Chunk.Size), y, Chunk.Mod(z, Chunk.Size))];
    }

    public byte GetBlockLight(int x, int y, int z) => Channel(x, y, z, static chunk => chunk.BlockLight);

    public byte GetSunGlow(int x, int y, int z) => Channel(x, y, z, static chunk => chunk.SunGlow);

    public byte SampleSky(int x, int y, int z)
    {
        byte column = GetSky(x, y, z);
        byte glow = GetSunGlow(x, y, z);
        return column > glow ? column : glow;
    }

    public bool SetBlock(int x, int y, int z, ushort id, byte state = 0)
    {
        if (y < 0 || y >= Chunk.Height || id == BlockIds.Boundary)
        {
            return false;
        }

        ChunkPos pos = CoordOf(x, z);
        if (!_chunks.TryGetValue(pos, out Chunk? chunk))
        {
            return false;
        }

        int index = Chunk.Index(Chunk.Mod(x, Chunk.Size), y, Chunk.Mod(z, Chunk.Size));
        if (chunk.Blocks[index] == id && chunk.State[index] == state)
        {
            return false;
        }

        byte oldState = chunk.State[index];
        ushort oldId = chunk.Blocks[index];
        byte[] oldSky = CaptureColumnSky(chunk, Chunk.Mod(x, Chunk.Size), Chunk.Mod(z, Chunk.Size));
        chunk.Blocks[index] = id;
        chunk.State[index] = id == BlockIds.Air ? (byte)0 : state;
        ExpandMesh(chunk, y);
        RelightColumn(chunk, Chunk.Mod(x, Chunk.Size), Chunk.Mod(z, Chunk.Size));
        QueueColumnSun(x, z, chunk, Chunk.Mod(x, Chunk.Size), Chunk.Mod(z, Chunk.Size), oldSky);
        QueueBlockLightChange(x, y, z, oldId, oldState);
        chunk.MeshDirty = true;
        MarkEdgeNeighbor(x, y, z);
        if (id == BlockIds.Water || id == BlockIds.Magma)
        {
            _flow.Enqueue(new FlowCell(x, y, z));
        }

        return true;
    }

    public void TickFlow(int budget)
    {
        while (budget > 0 && _flow.Count > 0)
        {
            budget--;
            FlowCell cell = _flow.Dequeue();
            ushort fluid = GetBlock(cell.X, cell.Y, cell.Z);
            if (fluid != BlockIds.Water && fluid != BlockIds.Magma)
            {
                continue;
            }

            int distance = GetState(cell.X, cell.Y, cell.Z);
            if (GetBlock(cell.X, cell.Y - 1, cell.Z) == BlockIds.Air)
            {
                SetBlock(cell.X, cell.Y - 1, cell.Z, fluid, (byte)distance);
                continue;
            }

            if (distance >= 4)
            {
                continue;
            }

            TryFlow(fluid, cell.X + 1, cell.Y, cell.Z, distance + 1);
            TryFlow(fluid, cell.X - 1, cell.Y, cell.Z, distance + 1);
            TryFlow(fluid, cell.X, cell.Y, cell.Z + 1, distance + 1);
            TryFlow(fluid, cell.X, cell.Y, cell.Z - 1, distance + 1);
        }
    }

    void TryFlow(ushort fluid, int x, int y, int z, int distance)
    {
        if (GetBlock(x, y, z) != BlockIds.Air)
        {
            return;
        }

        ushort below = GetBlock(x, y - 1, z);
        if (below == BlockIds.Air)
        {
            return;
        }

        SetBlock(x, y, z, fluid, (byte)distance);
    }

    public void Generate(ChunkPos pos)
    {
        Chunk chunk = GetOrCreate(pos);
        TerrainGenerator.Fill(chunk, pos);
        SeedColumnSun(pos);
        chunk.MeshDirty = true;
        MarkNeighborMeshes(pos);
    }

    public void SettleLight()
    {
        int guard = 200000;
        while (guard > 0 && (_blockRemove.Count > 0 || _blockAdd.Count > 0 || _sunRemove.Count > 0 || _sunAdd.Count > 0))
        {
            TickLight(1024);
            guard -= 1024;
        }
    }

    public void TickLight(int budget)
    {
        while (budget > 0 && _blockRemove.Count > 0)
        {
            budget--;
            SpreadRemove(_blockRemove.Dequeue(), block: true);
        }

        while (budget > 0 && _blockAdd.Count > 0)
        {
            budget--;
            SpreadAdd(_blockAdd.Dequeue(), block: true);
        }

        while (budget > 0 && _sunRemove.Count > 0)
        {
            budget--;
            SpreadRemove(_sunRemove.Dequeue(), block: false);
        }

        while (budget > 0 && _sunAdd.Count > 0)
        {
            budget--;
            SpreadAdd(_sunAdd.Dequeue(), block: false);
        }
    }

    readonly record struct FlowCell(int X, int Y, int Z);

    readonly record struct LightCell(int X, int Y, int Z, byte Level);

    public static ChunkPos CoordOf(int x, int z) =>
        new(Chunk.FloorDiv(x, Chunk.Size), Chunk.FloorDiv(z, Chunk.Size));

    static void ExpandMesh(Chunk chunk, int y)
    {
        int min = Math.Max(0, y - 1);
        int max = Math.Min(Chunk.Height - 1, y + 1);
        if (min < chunk.MeshMinY)
        {
            chunk.MeshMinY = min;
        }

        if (max > chunk.MeshMaxY)
        {
            chunk.MeshMaxY = max;
        }
    }

    static void RelightColumn(Chunk chunk, int localX, int localZ)
    {
        byte sun = 15;
        for (int y = Chunk.Height - 1; y >= chunk.MeshMinY; y--)
        {
            int index = Chunk.Index(localX, y, localZ);
            if (BlockCatalog.BlocksSun(chunk.Blocks[index]))
            {
                sun = 0;
            }

            chunk.Sky[index] = sun;
        }
    }

    byte Channel(int x, int y, int z, System.Func<Chunk, byte[]> pick)
    {
        if (y < 0 || y >= Chunk.Height)
        {
            return 0;
        }

        if (!_chunks.TryGetValue(CoordOf(x, z), out Chunk? chunk))
        {
            return 0;
        }

        return pick(chunk)[Chunk.Index(Chunk.Mod(x, Chunk.Size), y, Chunk.Mod(z, Chunk.Size))];
    }

    bool TryLightCell(int x, int y, int z, out Chunk chunk, out int index)
    {
        chunk = null!;
        index = 0;
        if (y < 0 || y >= Chunk.Height || !_chunks.TryGetValue(CoordOf(x, z), out chunk!))
        {
            return false;
        }

        index = Chunk.Index(Chunk.Mod(x, Chunk.Size), y, Chunk.Mod(z, Chunk.Size));
        return true;
    }

    static byte[] CaptureColumnSky(Chunk chunk, int localX, int localZ)
    {
        var sky = new byte[Chunk.Height];
        for (int y = 0; y < Chunk.Height; y++)
        {
            sky[y] = chunk.Sky[Chunk.Index(localX, y, localZ)];
        }

        return sky;
    }

    void QueueColumnSun(int x, int z, Chunk chunk, int localX, int localZ, byte[] oldSky)
    {
        for (int y = 0; y < Chunk.Height; y++)
        {
            byte next = chunk.Sky[Chunk.Index(localX, y, localZ)];
            if (next > oldSky[y])
            {
                _sunAdd.Enqueue(new LightCell(x, y, z, next));
            }
            else if (next < oldSky[y])
            {
                _sunRemove.Enqueue(new LightCell(x, y, z, oldSky[y]));
            }
        }

        SettleLight();
    }

    void QueueBlockLightChange(int x, int y, int z, ushort oldId, byte oldState)
    {
        if (!TryLightCell(x, y, z, out Chunk chunk, out int index))
        {
            return;
        }

        byte oldEmit = BlockCatalog.EmittedLight(oldId, oldState);
        byte nextEmit = BlockCatalog.EmittedLight(chunk.Blocks[index], chunk.State[index]);
        byte oldLight = chunk.BlockLight[index];
        if (!BlockCatalog.PassesLight(chunk.Blocks[index]))
        {
            chunk.BlockLight[index] = 0;
            if (oldLight > 0)
            {
                _blockRemove.Enqueue(new LightCell(x, y, z, oldLight));
            }
        }
        else if (oldLight > nextEmit)
        {
            chunk.BlockLight[index] = nextEmit;
            _blockRemove.Enqueue(new LightCell(x, y, z, oldLight));
        }

        if (nextEmit > chunk.BlockLight[index])
        {
            chunk.BlockLight[index] = nextEmit;
            _blockAdd.Enqueue(new LightCell(x, y, z, nextEmit));
        }
        else if (oldEmit > 0 && nextEmit == 0 && oldLight == 0)
        {
            _blockRemove.Enqueue(new LightCell(x, y, z, oldEmit));
        }

        SettleLight();
    }

    void SeedColumnSun(ChunkPos pos)
    {
        int originX = pos.X * Chunk.Size;
        int originZ = pos.Z * Chunk.Size;
        if (!_chunks.TryGetValue(pos, out Chunk? chunk))
        {
            return;
        }

        for (int lz = 0; lz < Chunk.Size; lz++)
        {
            for (int lx = 0; lx < Chunk.Size; lx++)
            {
                for (int y = chunk.MeshMinY; y <= chunk.MeshMaxY; y++)
                {
                    byte sky = chunk.Sky[Chunk.Index(lx, y, lz)];
                    if (sky > 1)
                    {
                        _sunAdd.Enqueue(new LightCell(originX + lx, y, originZ + lz, sky));
                    }
                }
            }
        }

        SettleLight();
    }

    void SpreadAdd(LightCell cell, bool block)
    {
        if (!TryLightCell(cell.X, cell.Y, cell.Z, out Chunk chunk, out int index))
        {
            return;
        }

        byte[] channel = block ? chunk.BlockLight : chunk.SunGlow;
        byte here = block ? channel[index] : Max(chunk.Sky[index], channel[index]);
        if (cell.Level > here && block)
        {
            channel[index] = cell.Level;
            here = cell.Level;
            chunk.MeshDirty = true;
        }

        if (here < 2)
        {
            return;
        }

        PushNeighbor(cell.X + 1, cell.Y, cell.Z, here, block);
        PushNeighbor(cell.X - 1, cell.Y, cell.Z, here, block);
        PushNeighbor(cell.X, cell.Y, cell.Z + 1, here, block);
        PushNeighbor(cell.X, cell.Y, cell.Z - 1, here, block);
        PushNeighbor(cell.X, cell.Y + 1, cell.Z, here, block);
        PushNeighbor(cell.X, cell.Y - 1, cell.Z, here, block);
    }

    void PushNeighbor(int x, int y, int z, byte source, bool block)
    {
        if (!TryLightCell(x, y, z, out Chunk chunk, out int index))
        {
            return;
        }

        ushort id = chunk.Blocks[index];
        if (!BlockCatalog.PassesLight(id))
        {
            return;
        }

        int next = source - 1 - BlockCatalog.LightAttenuation(id);
        if (next < 1)
        {
            return;
        }

        byte[] channel = block ? chunk.BlockLight : chunk.SunGlow;
        byte current = block ? channel[index] : Max(chunk.Sky[index], channel[index]);
        if (next <= current)
        {
            return;
        }

        if (!block && next <= chunk.Sky[index])
        {
            return;
        }

        channel[index] = (byte)next;
        chunk.MeshDirty = true;
        Queue<LightCell> add = block ? _blockAdd : _sunAdd;
        add.Enqueue(new LightCell(x, y, z, (byte)next));
        DirtyAround(x, y, z);
    }

    void SpreadRemove(LightCell cell, bool block)
    {
        if (cell.Level < 2)
        {
            return;
        }

        PullNeighbor(cell.X + 1, cell.Y, cell.Z, cell.Level, block);
        PullNeighbor(cell.X - 1, cell.Y, cell.Z, cell.Level, block);
        PullNeighbor(cell.X, cell.Y, cell.Z + 1, cell.Level, block);
        PullNeighbor(cell.X, cell.Y, cell.Z - 1, cell.Level, block);
        PullNeighbor(cell.X, cell.Y + 1, cell.Z, cell.Level, block);
        PullNeighbor(cell.X, cell.Y - 1, cell.Z, cell.Level, block);
    }

    void PullNeighbor(int x, int y, int z, byte removed, bool block)
    {
        if (!TryLightCell(x, y, z, out Chunk chunk, out int index))
        {
            return;
        }

        byte[] channel = block ? chunk.BlockLight : chunk.SunGlow;
        byte current = channel[index];
        if (current == 0 || current >= removed)
        {
            if (current >= removed && current > 1)
            {
                Queue<LightCell> add = block ? _blockAdd : _sunAdd;
                add.Enqueue(new LightCell(x, y, z, current));
            }

            return;
        }

        byte emit = block ? BlockCatalog.EmittedLight(chunk.Blocks[index], chunk.State[index]) : chunk.Sky[index];
        channel[index] = emit;
        chunk.MeshDirty = true;
        Queue<LightCell> remove = block ? _blockRemove : _sunRemove;
        remove.Enqueue(new LightCell(x, y, z, current));
        if (emit > 1)
        {
            Queue<LightCell> add = block ? _blockAdd : _sunAdd;
            add.Enqueue(new LightCell(x, y, z, emit));
        }

        DirtyAround(x, y, z);
    }

    static byte Max(byte a, byte b) => a > b ? a : b;

    void DirtyAround(int x, int y, int z)
    {
        TouchNeighbor(x, y, z);
        MarkEdgeNeighbor(x, y, z);
    }

    void MarkEdgeNeighbor(int x, int y, int z)
    {
        int localX = Chunk.Mod(x, Chunk.Size);
        int localZ = Chunk.Mod(z, Chunk.Size);
        if (localX == 0)
        {
            TouchNeighbor(x - 1, y, z);
        }

        if (localX == Chunk.Size - 1)
        {
            TouchNeighbor(x + 1, y, z);
        }

        if (localZ == 0)
        {
            TouchNeighbor(x, y, z - 1);
        }

        if (localZ == Chunk.Size - 1)
        {
            TouchNeighbor(x, y, z + 1);
        }
    }

    void TouchNeighbor(int x, int y, int z)
    {
        if (!_chunks.TryGetValue(CoordOf(x, z), out Chunk? chunk))
        {
            return;
        }

        ExpandMesh(chunk, y);
        chunk.MeshDirty = true;
    }

    void MarkNeighborMeshes(ChunkPos pos)
    {
        Dirty(new ChunkPos(pos.X + 1, pos.Z));
        Dirty(new ChunkPos(pos.X - 1, pos.Z));
        Dirty(new ChunkPos(pos.X, pos.Z + 1));
        Dirty(new ChunkPos(pos.X, pos.Z - 1));
    }

    void Dirty(ChunkPos pos)
    {
        if (_chunks.TryGetValue(pos, out Chunk? chunk))
        {
            chunk.MeshDirty = true;
        }
    }
}
