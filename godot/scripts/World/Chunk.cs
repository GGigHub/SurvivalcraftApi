namespace SurvivalcraftGodot;

public readonly record struct ChunkPos(int X, int Z);

public sealed class Chunk
{
    public const int Size = 16;
    public const int Height = 256;
    public const int Volume = Size * Height * Size;

    public ushort[] Blocks { get; } = new ushort[Volume];
    public byte[] State { get; } = new byte[Volume];
    public byte[] Sky { get; } = new byte[Volume];
    public byte[] SunGlow { get; } = new byte[Volume];
    public byte[] BlockLight { get; } = new byte[Volume];
    public bool MeshDirty { get; set; } = true;
    public int MeshMinY { get; set; }
    public int MeshMaxY { get; set; } = -1;

    public static int Index(int localX, int y, int localZ) =>
        y + (localX * Height) + (localZ * Height * Size);

    public static int FloorDiv(int value, int size)
    {
        int q = value / size;
        if (value < 0 && value % size != 0)
        {
            q--;
        }

        return q;
    }

    public static int Mod(int value, int size)
    {
        int r = value % size;
        return r < 0 ? r + size : r;
    }
}
