using System;

namespace SurvivalcraftGodot;

public static class TerrainGenerator
{
    public const int Seed = 1;

    public static int SurfaceY(int x, int z) => BiomeFill.Height(Seed, x, z);

    public static void Fill(Chunk chunk, ChunkPos pos)
    {
        int originX = pos.X * Chunk.Size;
        int originZ = pos.Z * Chunk.Size;
        int minSurface = int.MaxValue;
        int maxSurface = 0;
        for (int lz = -1; lz <= Chunk.Size; lz++)
        {
            for (int lx = -1; lx <= Chunk.Size; lx++)
            {
                int surface = SurfaceY(originX + lx, originZ + lz);
                if (surface < minSurface)
                {
                    minSurface = surface;
                }

                if (surface > maxSurface)
                {
                    maxSurface = surface;
                }
            }
        }

        chunk.MeshMinY = Math.Max(0, minSurface - 1);
        chunk.MeshMaxY = Math.Min(Chunk.Height - 1, maxSurface + 1);

        for (int lz = 0; lz < Chunk.Size; lz++)
        {
            for (int lx = 0; lx < Chunk.Size; lx++)
            {
                BiomeColumn column = BiomeFill.Sample(Seed, originX + lx, originZ + lz);
                int surface = column.SurfaceY;
                for (int y = 0; y < Chunk.Height; y++)
                {
                    ushort id = BlockIds.Air;
                    if (y < surface)
                    {
                        int depth = surface - 1 - y;
                        id = depth < column.UndergroundIds.Length
                            ? column.UndergroundIds[depth]
                            : BlockIds.Stone;
                    }
                    else if (y == surface)
                    {
                        id = column.SurfaceId;
                    }

                    int index = Chunk.Index(lx, y, lz);
                    chunk.Blocks[index] = id;
                    if (y >= chunk.MeshMinY)
                    {
                        chunk.Sky[index] = 0;
                    }
                }

            }
        }

        PlantTrees(chunk, originX, originZ);
        PlantTallGrass(chunk, originX, originZ);
        for (int lz = 0; lz < Chunk.Size; lz++)
        {
            for (int lx = 0; lx < Chunk.Size; lx++)
            {
                byte sun = 15;
                for (int y = Chunk.Height - 1; y >= chunk.MeshMinY; y--)
                {
                    int index = Chunk.Index(lx, y, lz);
                    if (BlockCatalog.BlocksSun(chunk.Blocks[index]))
                    {
                        sun = 0;
                    }

                    chunk.Sky[index] = sun;
                }
            }
        }
    }

    static void PlantTrees(Chunk chunk, int originX, int originZ)
    {
        int top = chunk.MeshMaxY;
        for (int tz = originZ - 2; tz <= originZ + Chunk.Size + 1; tz++)
        {
            for (int tx = originX - 2; tx <= originX + Chunk.Size + 1; tx++)
            {
                if (!IsTreeSpot(tx, tz))
                {
                    continue;
                }

                int surface = SurfaceY(tx, tz);
                int trunkTop = surface + 4;
                if (trunkTop + 2 > top)
                {
                    top = trunkTop + 2;
                }

                for (int dy = -2; dy <= 2; dy++)
                {
                    for (int dz = -2; dz <= 2; dz++)
                    {
                        for (int dx = -2; dx <= 2; dx++)
                        {
                            int wx = tx + dx;
                            int wy = trunkTop + dy;
                            int wz = tz + dz;
                            if (!TryLocal(originX, originZ, wx, wy, wz, out int lx, out int lz))
                            {
                                continue;
                            }

                            int index = Chunk.Index(lx, wy, lz);
                            bool trunk = dx == 0 && dz == 0 && wy > surface && wy <= trunkTop;
                            if (trunk)
                            {
                                chunk.Blocks[index] = BlockIds.Log;
                            }
                            else if (Math.Abs(dx) + Math.Abs(dz) <= 3 && wy > surface && chunk.Blocks[index] == BlockIds.Air)
                            {
                                chunk.Blocks[index] = BlockIds.Leaves;
                            }
                        }
                    }
                }
            }
        }

        chunk.MeshMaxY = Math.Min(Chunk.Height - 1, Math.Max(chunk.MeshMaxY, top));
    }

    static void PlantTallGrass(Chunk chunk, int originX, int originZ)
    {
        for (int lz = 0; lz < Chunk.Size; lz++)
        {
            for (int lx = 0; lx < Chunk.Size; lx++)
            {
                int wx = originX + lx;
                int wz = originZ + lz;
                BiomeColumn column = BiomeFill.Sample(Seed, wx, wz);
                if (column.SurfaceId != BlockIds.Grass || Hash(wx, wz) % 3 != 0)
                {
                    continue;
                }

                int y = column.SurfaceY + 1;
                int index = Chunk.Index(lx, y, lz);
                if (chunk.Blocks[index] != BlockIds.Air)
                {
                    continue;
                }

                chunk.Blocks[index] = BlockIds.Plant;
                if (y > chunk.MeshMaxY)
                {
                    chunk.MeshMaxY = y;
                }
            }
        }
    }

    static bool TryLocal(int originX, int originZ, int x, int y, int z, out int localX, out int localZ)
    {
        localX = x - originX;
        localZ = z - originZ;
        return y >= 0 && y < Chunk.Height && localX >= 0 && localZ >= 0 && localX < Chunk.Size && localZ < Chunk.Size;
    }

    static bool IsTreeSpot(int x, int z)
    {
        if (Hash(x, z) % 29 != 0)
        {
            return false;
        }

        int surface = SurfaceY(x, z);
        ushort surfaceId = BiomeFill.Sample(Seed, x, z).SurfaceId;
        return surfaceId == BlockIds.Grass
            && Math.Abs(SurfaceY(x + 1, z) - surface) <= 1
            && Math.Abs(SurfaceY(x - 1, z) - surface) <= 1
            && Math.Abs(SurfaceY(x, z + 1) - surface) <= 1
            && Math.Abs(SurfaceY(x, z - 1) - surface) <= 1;
    }

    static int Hash(int x, int z)
    {
        unchecked
        {
            int n = (x * 374761393) + (z * 668265263);
            n = (n ^ (n >> 13)) * 1274126177;
            return n & 0x7fffffff;
        }
    }
}
