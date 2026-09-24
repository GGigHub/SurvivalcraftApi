namespace SurvivalcraftGodot.Simulation;

/// <summary>原版 CellFace 朝向：0=+Z, 1=+X, 2=-Z, 3=-X, 4=+Y, 5=-Y。</summary>
public static class CellFaces
{
    public static readonly (int X, int Y, int Z)[] Offsets =
    [
        (0, 0, 1),
        (1, 0, 0),
        (0, 0, -1),
        (-1, 0, 0),
        (0, 1, 0),
        (0, -1, 0)
    ];

    public static int Opposite(int face) => face switch
    {
        0 => 2,
        1 => 3,
        2 => 0,
        3 => 1,
        4 => 5,
        _ => 4
    };

    public static (int X, int Y, int Z) Offset(int face) => Offsets[face];
}

public enum ElectricConnectorType
{
    Input = 0,
    Output = 1,
    InputOutput = 2
}

public readonly struct GridPos : System.IEquatable<GridPos>
{
    public readonly int X;
    public readonly int Y;
    public readonly int Z;

    public GridPos(int x, int y, int z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public GridPos Offset(int face)
    {
        var (dx, dy, dz) = CellFaces.Offset(face);
        return new GridPos(X + dx, Y + dy, Z + dz);
    }

    public bool Equals(GridPos other) => X == other.X && Y == other.Y && Z == other.Z;
    public override bool Equals(object? obj) => obj is GridPos other && Equals(other);
    public override int GetHashCode() => System.HashCode.Combine(X, Y, Z);
    public override string ToString() => $"({X},{Y},{Z})";
}

/// <summary>纯 C# 小型格子世界，不依赖 Godot 场景或 VoxelWorld。</summary>
public sealed class SimGrid
{
    readonly int[,,] _cells;
    public int SizeX { get; }
    public int SizeY { get; }
    public int SizeZ { get; }

    public SimGrid(int sizeX, int sizeY, int sizeZ)
    {
        SizeX = sizeX;
        SizeY = sizeY;
        SizeZ = sizeZ;
        _cells = new int[sizeX, sizeY, sizeZ];
    }

    public bool InBounds(int x, int y, int z) =>
        x >= 0 && y >= 0 && z >= 0 && x < SizeX && y < SizeY && z < SizeZ;

    public bool InBounds(GridPos p) => InBounds(p.X, p.Y, p.Z);

    public int Get(int x, int y, int z) => InBounds(x, y, z) ? _cells[x, y, z] : ElectricIds.Air;

    public int Get(GridPos p) => Get(p.X, p.Y, p.Z);

    public void Set(int x, int y, int z, int id)
    {
        if (InBounds(x, y, z))
        {
            _cells[x, y, z] = id;
        }
    }

    public void Set(GridPos p, int id) => Set(p.X, p.Y, p.Z, id);
}
