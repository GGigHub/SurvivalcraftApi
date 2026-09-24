using Godot;
using System.Collections.Generic;

namespace SurvivalcraftGodot;

public sealed class ChunkMeshData
{
    public Vector3[] Vertices { get; init; } = System.Array.Empty<Vector3>();
    public Color[] Colors { get; init; } = System.Array.Empty<Color>();
    public Vector2[] Uvs { get; init; } = System.Array.Empty<Vector2>();
    public Vector2[] Uv2 { get; init; } = System.Array.Empty<Vector2>();
    public int[] Indices { get; init; } = System.Array.Empty<int>();
}

public static class ChunkMesher
{
    static readonly Vector3I[] Normals =
    {
        new(1, 0, 0), new(-1, 0, 0),
        new(0, 1, 0), new(0, -1, 0),
        new(0, 0, 1), new(0, 0, -1),
    };

    static readonly Vector3[][] Corners =
    {
        new Vector3[] { new(1, 0, 0), new(1, 1, 0), new(1, 1, 1), new(1, 0, 1) },
        new Vector3[] { new(0, 0, 1), new(0, 1, 1), new(0, 1, 0), new(0, 0, 0) },
        new Vector3[] { new(0, 1, 0), new(0, 1, 1), new(1, 1, 1), new(1, 1, 0) },
        new Vector3[] { new(0, 0, 1), new(0, 0, 0), new(1, 0, 0), new(1, 0, 1) },
        new Vector3[] { new(1, 0, 1), new(1, 1, 1), new(0, 1, 1), new(0, 0, 1) },
        new Vector3[] { new(0, 0, 0), new(0, 1, 0), new(1, 1, 0), new(1, 0, 0) },
    };

    public static ChunkMeshData Build(VoxelWorld world, ChunkPos pos)
    {
        if (!world.TryGetChunk(pos, out Chunk? chunk))
        {
            return new ChunkMeshData();
        }

        var vertices = new List<Vector3>(4096);
        var colors = new List<Color>(4096);
        var uvs = new List<Vector2>(4096);
        var uv2 = new List<Vector2>(4096);
        var indices = new List<int>(6144);
        int originX = pos.X * Chunk.Size;
        int originZ = pos.Z * Chunk.Size;

        for (int lz = 0; lz < Chunk.Size; lz++)
        {
            for (int lx = 0; lx < Chunk.Size; lx++)
            {
                int y0 = chunk.MeshMinY;
                int y1 = chunk.MeshMaxY;
                for (int y = y0; y <= y1; y++)
                {
                    ushort id = chunk.Blocks[Chunk.Index(lx, y, lz)];
                    if (id == BlockIds.Air)
                    {
                        continue;
                    }

                    int wx = originX + lx;
                    int wz = originZ + lz;
                    byte state = chunk.State[Chunk.Index(lx, y, lz)];
                    switch (BlockCatalog.ShapeOf(id))
                    {
                        case BlockShape.Cross:
                            AddCross(world, vertices, uvs, uv2, colors, indices, id, wx, y, wz, lx, y, lz, 0f, 1f);
                            break;
                        case BlockShape.Torch:
                            AddTorch(world, vertices, uvs, uv2, colors, indices, wx, y, wz, lx, y, lz);
                            break;
                        case BlockShape.Slab:
                            if (!AddPlacedModel(world, vertices, uvs, uv2, colors, indices, id, "Slab", 0, wx, y, wz, lx, y, lz, false))
                            {
                                AddBox(world, vertices, uvs, uv2, colors, indices, id, wx, y, wz, lx, y, lz, Vector3.Zero, new Vector3(1, 0.5f, 1));
                            }
                            break;
                        case BlockShape.Carpet:
                            AddBox(world, vertices, uvs, uv2, colors, indices, id, wx, y, wz, lx, y, lz, Vector3.Zero, new Vector3(1, 0.06f, 1));
                            break;
                        case BlockShape.Trapdoor:
                            if (!AddPlacedModel(world, vertices, uvs, uv2, colors, indices, id, "Trapdoor", state, wx, y, wz, lx, y, lz, false))
                            {
                                AddBox(world, vertices, uvs, uv2, colors, indices, id, wx, y, wz, lx, y, lz, new Vector3(0, 0.84f, 0), Vector3.One);
                            }
                            break;
                        case BlockShape.Liquid:
                            AddBox(world, vertices, uvs, uv2, colors, indices, id, wx, y, wz, lx, y, lz, Vector3.Zero, new Vector3(1, 0.88f, 1));
                            break;
                        case BlockShape.Door:
                            if (!AddPlacedModel(world, vertices, uvs, uv2, colors, indices, id, "Door", BlockShapes.DoorFacing(state), wx, y, wz, lx, y, lz, false, new Vector3(0f, 0f, 0.4375f)))
                            {
                                AddOrientedBox(world, vertices, uvs, uv2, colors, indices, id, state, wx, y, wz, lx, y, lz, Vector3.Zero, new Vector3(0.16f, 1, 1));
                            }
                            break;
                        case BlockShape.Stairs:
                            if (!AddPlacedModel(world, vertices, uvs, uv2, colors, indices, id, "Stairs", state, wx, y, wz, lx, y, lz, false))
                            {
                                AddOrientedBox(world, vertices, uvs, uv2, colors, indices, id, state, wx, y, wz, lx, y, lz, Vector3.Zero, new Vector3(1, 0.5f, 1));
                                AddOrientedBox(world, vertices, uvs, uv2, colors, indices, id, state, wx, y, wz, lx, y, lz, new Vector3(0.5f, 0.5f, 0), Vector3.One);
                            }
                            break;
                        case BlockShape.Fence:
                            bool post = AddPlacedModel(world, vertices, uvs, uv2, colors, indices, id, "Post", 0, wx, y, wz, lx, y, lz, false);
                            bool planks = AddPlacedModel(world, vertices, uvs, uv2, colors, indices, id, "Planks", 0, wx, y, wz, lx, y, lz, false);
                            if (!post && !planks)
                            {
                                AddBox(world, vertices, uvs, uv2, colors, indices, id, wx, y, wz, lx, y, lz, new Vector3(0.37f, 0, 0.37f), new Vector3(0.63f, 1, 0.63f));
                                AddBox(world, vertices, uvs, uv2, colors, indices, id, wx, y, wz, lx, y, lz, new Vector3(0, 0.4f, 0.42f), new Vector3(1, 0.55f, 0.58f));
                                AddBox(world, vertices, uvs, uv2, colors, indices, id, wx, y, wz, lx, y, lz, new Vector3(0.42f, 0.75f, 0), new Vector3(0.58f, 0.9f, 1));
                            }
                            break;
                        case BlockShape.Solid:
            if (id == BlockIds.Furnace && AddPlacedModel(world, vertices, uvs, uv2, colors, indices, id, "Furnace", state & 3, wx, y, wz, lx, y, lz, false))
            {
                if ((state & 4) != 0)
                {
                    AddPlacedModel(world, vertices, uvs, uv2, colors, indices, id, "Flame", state & 3, wx, y, wz, lx, y, lz, true, new Vector3(0f, 0.25f, 0.15f));
                }

                break;
            }

            if (id == (ushort)SurvivalcraftGodot.Simulation.ElectricIds.Button
                && AddPlacedModel(world, vertices, uvs, uv2, colors, indices, id, "Button", state & 3, wx, y, wz, lx, y, lz, false))
            {
                break;
            }

            AddBox(world, vertices, uvs, uv2, colors, indices, id, wx, y, wz, lx, y, lz, Vector3.Zero, Vector3.One);
            break;
                    }
                }
            }
        }

        return new ChunkMeshData
        {
            Vertices = vertices.ToArray(),
            Colors = colors.ToArray(),
            Uvs = uvs.ToArray(),
            Uv2 = uv2.ToArray(),
            Indices = indices.ToArray(),
        };
    }

    static void AddCross(
        VoxelWorld world,
        List<Vector3> vertices,
        List<Vector2> uvs,
        List<Vector2> uv2,
        List<Color> colors,
        List<int> indices,
        ushort id,
        int wx,
        int wy,
        int wz,
        int lx,
        int y,
        int lz,
        float inset,
        float height)
    {
        Color tint = Lit(id, 2, world.SampleSky(wx, wy, wz), world.GetBlockLight(wx, wy, wz), out float blockCurve);
        Vector3 block = new(lx, y, lz);
        float a = inset;
        float b = 1f - inset;
        BlockCatalog.SlotUv(BlockCatalog.FaceSlot(id, 2), out float u0, out float v0, out float u1, out float v1);
        var blockUv = new Vector2(blockCurve, 0f);
        AddQuad(vertices, uvs, uv2, colors, indices, block, tint, blockUv,
            new Vector3(a, 0, a), new Vector3(b, 0, b), new Vector3(b, height, b), new Vector3(a, height, a),
            new Vector2(u0, v1), new Vector2(u1, v1), new Vector2(u1, v0), new Vector2(u0, v0));
        AddQuad(vertices, uvs, uv2, colors, indices, block, tint, blockUv,
            new Vector3(a, 0, b), new Vector3(a, height, b), new Vector3(b, height, a), new Vector3(b, 0, a),
            new Vector2(u0, v1), new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v1));
    }

    static void AddTorch(
        VoxelWorld world,
        List<Vector3> vertices,
        List<Vector2> uvs,
        List<Vector2> uv2,
        List<Color> colors,
        List<int> indices,
        int wx,
        int wy,
        int wz,
        int lx,
        int y,
        int lz)
    {
        if (AddPlacedModel(world, vertices, uvs, uv2, colors, indices, BlockIds.Torch, "Torch", 0, wx, wy, wz, lx, y, lz, false)
            && AddPlacedModel(world, vertices, uvs, uv2, colors, indices, BlockIds.Torch, "Flame", 0, wx, wy, wz, lx, y, lz, true))
        {
            return;
        }

        Color stick = Lit(BlockIds.Torch, 0, world.SampleSky(wx, wy, wz), world.GetBlockLight(wx, wy, wz), out float blockCurve);
        var woodUv = new Vector2(0.468f, 1f - 0.16f);
        var stickLight = new Vector2(blockCurve, 0f);
        Vector3 origin = new(lx, y, lz);
        AddBoxRaw(vertices, uvs, uv2, colors, indices, origin, stick, stickLight, woodUv, new Vector3(0.45f, 0f, 0.45f), new Vector3(0.55f, 0.55f, 0.55f));

        var flameUv = new Vector2(0.97f, 1f - 0.91f);
        var glow = new Vector2(1f, 0f);
        var hot = new Color(1f, 0.92f, 0.35f, 1f);
        var fire = new Color(1f, 0.32f, 0.02f, 1f);
        Vector3 top = new(0.5f, 0.74f, 0.5f);
        Vector3 bottom = new(0.5f, 0.46f, 0.5f);
        Vector3 east = new(0.58f, 0.58f, 0.5f);
        Vector3 west = new(0.42f, 0.58f, 0.5f);
        Vector3 north = new(0.5f, 0.58f, 0.42f);
        Vector3 south = new(0.5f, 0.58f, 0.58f);
        AddTri(vertices, uvs, uv2, colors, indices, origin, glow, flameUv, top, east, north, hot, fire, fire);
        AddTri(vertices, uvs, uv2, colors, indices, origin, glow, flameUv, top, north, west, hot, fire, fire);
        AddTri(vertices, uvs, uv2, colors, indices, origin, glow, flameUv, top, west, south, hot, fire, fire);
        AddTri(vertices, uvs, uv2, colors, indices, origin, glow, flameUv, top, south, east, hot, fire, fire);
        AddTri(vertices, uvs, uv2, colors, indices, origin, glow, flameUv, bottom, north, east, fire, fire, fire);
        AddTri(vertices, uvs, uv2, colors, indices, origin, glow, flameUv, bottom, west, north, fire, fire, fire);
        AddTri(vertices, uvs, uv2, colors, indices, origin, glow, flameUv, bottom, south, west, fire, fire, fire);
        AddTri(vertices, uvs, uv2, colors, indices, origin, glow, flameUv, bottom, east, south, fire, fire, fire);
    }

    static void AddBoxRaw(
        List<Vector3> vertices,
        List<Vector2> uvs,
        List<Vector2> uv2,
        List<Color> colors,
        List<int> indices,
        Vector3 origin,
        Color tint,
        Vector2 lightUv,
        Vector2 atlasUv,
        Vector3 min,
        Vector3 max)
    {
        AddQuad(vertices, uvs, uv2, colors, indices, origin, tint, lightUv,
            new Vector3(max.X, min.Y, min.Z), new Vector3(max.X, max.Y, min.Z), new Vector3(max.X, max.Y, max.Z), new Vector3(max.X, min.Y, max.Z),
            atlasUv, atlasUv, atlasUv, atlasUv);
        AddQuad(vertices, uvs, uv2, colors, indices, origin, tint, lightUv,
            new Vector3(min.X, min.Y, max.Z), new Vector3(min.X, max.Y, max.Z), new Vector3(min.X, max.Y, min.Z), new Vector3(min.X, min.Y, min.Z),
            atlasUv, atlasUv, atlasUv, atlasUv);
        AddQuad(vertices, uvs, uv2, colors, indices, origin, tint, lightUv,
            new Vector3(min.X, max.Y, min.Z), new Vector3(min.X, max.Y, max.Z), new Vector3(max.X, max.Y, max.Z), new Vector3(max.X, max.Y, min.Z),
            atlasUv, atlasUv, atlasUv, atlasUv);
        AddQuad(vertices, uvs, uv2, colors, indices, origin, tint, lightUv,
            new Vector3(min.X, min.Y, max.Z), new Vector3(min.X, min.Y, min.Z), new Vector3(max.X, min.Y, min.Z), new Vector3(max.X, min.Y, max.Z),
            atlasUv, atlasUv, atlasUv, atlasUv);
        AddQuad(vertices, uvs, uv2, colors, indices, origin, tint, lightUv,
            new Vector3(max.X, min.Y, max.Z), new Vector3(max.X, max.Y, max.Z), new Vector3(min.X, max.Y, max.Z), new Vector3(min.X, min.Y, max.Z),
            atlasUv, atlasUv, atlasUv, atlasUv);
        AddQuad(vertices, uvs, uv2, colors, indices, origin, tint, lightUv,
            new Vector3(min.X, min.Y, min.Z), new Vector3(min.X, max.Y, min.Z), new Vector3(max.X, max.Y, min.Z), new Vector3(max.X, min.Y, min.Z),
            atlasUv, atlasUv, atlasUv, atlasUv);
    }

    static void AddTri(
        List<Vector3> vertices,
        List<Vector2> uvs,
        List<Vector2> uv2,
        List<Color> colors,
        List<int> indices,
        Vector3 origin,
        Vector2 lightUv,
        Vector2 atlasUv,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Color ca,
        Color cb,
        Color cc)
    {
        int start = vertices.Count;
        vertices.Add(origin + a);
        vertices.Add(origin + b);
        vertices.Add(origin + c);
        uvs.Add(atlasUv);
        uvs.Add(atlasUv);
        uvs.Add(atlasUv);
        uv2.Add(lightUv);
        uv2.Add(lightUv);
        uv2.Add(lightUv);
        colors.Add(ca);
        colors.Add(cb);
        colors.Add(cc);
        indices.Add(start);
        indices.Add(start + 1);
        indices.Add(start + 2);
    }

    static bool AddPlacedModel(
        VoxelWorld world,
        List<Vector3> vertices,
        List<Vector2> uvs,
        List<Vector2> uv2,
        List<Color> colors,
        List<int> indices,
        ushort id,
        string geometry,
        int facing,
        int wx,
        int wy,
        int wz,
        int lx,
        int y,
        int lz,
        bool emissive,
        Vector3 shift = default)
    {
        if (!BlockModel.TryGet(geometry, out ModelMesh mesh) || mesh.Positions.Length < 3)
        {
            return false;
        }

        byte sky = world.SampleSky(wx, wy, wz);
        byte blockLight = world.GetBlockLight(wx, wy, wz);
        ConsiderLight(world, wx + 1, wy, wz, ref sky, ref blockLight);
        ConsiderLight(world, wx - 1, wy, wz, ref sky, ref blockLight);
        ConsiderLight(world, wx, wy + 1, wz, ref sky, ref blockLight);
        ConsiderLight(world, wx, wy - 1, wz, ref sky, ref blockLight);
        ConsiderLight(world, wx, wy, wz + 1, ref sky, ref blockLight);
        ConsiderLight(world, wx, wy, wz - 1, ref sky, ref blockLight);
        Color tint;
        float blockCurve;
        if (emissive)
        {
            tint = Colors.White;
            blockCurve = 1f;
        }
        else
        {
            tint = Lit(id, 0, sky, blockLight, out blockCurve);
        }

        var light = new Vector2(blockCurve, 0f);
        Vector3 origin = new(lx, y, lz);
        for (int i = 0; i + 2 < mesh.Positions.Length; i += 3)
        {
            AddTri(
                vertices, uvs, uv2, colors, indices, origin, light, mesh.Uvs[i],
                BlockShapes.RotatePoint(facing, mesh.Positions[i] + shift + new Vector3(0.5f, 0f, 0.5f)),
                BlockShapes.RotatePoint(facing, mesh.Positions[i + 1] + shift + new Vector3(0.5f, 0f, 0.5f)),
                BlockShapes.RotatePoint(facing, mesh.Positions[i + 2] + shift + new Vector3(0.5f, 0f, 0.5f)),
                tint, tint, tint);
            uvs[^3] = ToBlockTile(id, mesh.Uvs[i]);
            uvs[^2] = ToBlockTile(id, mesh.Uvs[i + 1]);
            uvs[^1] = ToBlockTile(id, mesh.Uvs[i + 2]);
        }

        return true;
    }

    static void ConsiderLight(VoxelWorld world, int x, int y, int z, ref byte sky, ref byte blockLight)
    {
        byte nextSky = world.SampleSky(x, y, z);
        byte nextBlock = world.GetBlockLight(x, y, z);
        if (nextSky > sky)
        {
            sky = nextSky;
        }

        if (nextBlock > blockLight)
        {
            blockLight = nextBlock;
        }
    }

    static Vector2 ToBlockTile(ushort id, Vector2 uv)
    {
        if (uv.X > 0.07f || uv.Y > 0.07f)
        {
            return uv;
        }

        int slot = BlockCatalog.FaceSlot(id, 0);
        return new Vector2(uv.X + (slot % 16) / 16f, uv.Y + (slot / 16) / 16f);
    }

    static void AddOrientedBox(
        VoxelWorld world,
        List<Vector3> vertices,
        List<Vector2> uvs,
        List<Vector2> uv2,
        List<Color> colors,
        List<int> indices,
        ushort id,
        byte state,
        int wx,
        int wy,
        int wz,
        int lx,
        int y,
        int lz,
        Vector3 min,
        Vector3 max)
    {
        BlockShapes.Rotate(state, ref min, ref max);
        AddBox(world, vertices, uvs, uv2, colors, indices, id, wx, wy, wz, lx, y, lz, min, max);
    }

    static void AddBox(
        VoxelWorld world,
        List<Vector3> vertices,
        List<Vector2> uvs,
        List<Vector2> uv2,
        List<Color> colors,
        List<int> indices,
        ushort id,
        int wx,
        int wy,
        int wz,
        int lx,
        int y,
        int lz,
        Vector3 min,
        Vector3 max)
    {
        Vector3 block = new(lx, y, lz);
        for (int face = 0; face < 6; face++)
        {
            Vector3I normal = Normals[face];
            Vector3[] corners = Corners[face];
            float plane = normal.X != 0
                ? (normal.X > 0 ? max.X : min.X)
                : normal.Y != 0
                    ? (normal.Y > 0 ? max.Y : min.Y)
                    : (normal.Z > 0 ? max.Z : min.Z);
            bool onCellEdge = (normal.X > 0 || normal.Y > 0 || normal.Z > 0)
                ? plane >= 0.999f
                : plane <= 0.001f;
            ushort neighbor = world.GetBlock(wx + normal.X, wy + normal.Y, wz + normal.Z);
            if (onCellEdge && (BlockCatalog.FullCell(neighbor) || (id == neighbor && BlockCatalog.ShapeOf(id) == BlockShape.Liquid)))
            {
                continue;
            }

            int sx = wx + normal.X;
            int sy = wy + normal.Y;
            int sz = wz + normal.Z;
            byte sky = world.SampleSky(sx, sy, sz);
            byte blockLight = world.GetBlockLight(sx, sy, sz);
            if (sky == 0 && blockLight == 0)
            {
                sky = world.SampleSky(wx, wy, wz);
                blockLight = world.GetBlockLight(wx, wy, wz);
            }

            Color tint = Lit(id, face, sky, blockLight, out float blockCurve);
            if (id == (ushort)SurvivalcraftGodot.Simulation.ElectricIds.LightBulb && world.GetState(wx, wy, wz) == 1)
            {
                tint = new Color(tint.R, tint.G, tint.B, tint.A);
                tint = new Color(1f, 0.95f, 0.4f, tint.A);
            }
            BlockCatalog.SlotUv(BlockCatalog.FaceSlot(id, face), out float u0, out float v0, out float u1, out float v1);
            var fitted = new Vector3[4];
            var faceUv = new Vector2[4];
            for (int i = 0; i < 4; i++)
            {
                fitted[i] = new Vector3(
                    Mathf.Lerp(min.X, max.X, corners[i].X),
                    Mathf.Lerp(min.Y, max.Y, corners[i].Y),
                    Mathf.Lerp(min.Z, max.Z, corners[i].Z));
                faceUv[i] = UvOnFace(corners[i], normal, u0, v0, u1, v1);
            }

            AddQuad(vertices, uvs, uv2, colors, indices, block, tint, new Vector2(blockCurve, 0f), fitted[0], fitted[1], fitted[2], fitted[3], faceUv[0], faceUv[1], faceUv[2], faceUv[3]);
        }
    }

    static Vector2 UvOnFace(Vector3 corner, Vector3I normal, float u0, float v0, float u1, float v1)
    {
        if (normal.Y != 0)
        {
            return new Vector2(Mathf.Lerp(u0, u1, corner.X), Mathf.Lerp(v0, v1, corner.Z));
        }

        if (normal.X != 0)
        {
            return new Vector2(Mathf.Lerp(u0, u1, corner.Z), Mathf.Lerp(v1, v0, corner.Y));
        }

        return new Vector2(Mathf.Lerp(u0, u1, corner.X), Mathf.Lerp(v1, v0, corner.Y));
    }

    static void AddQuad(
        List<Vector3> vertices,
        List<Vector2> uvs,
        List<Vector2> uv2,
        List<Color> colors,
        List<int> indices,
        Vector3 block,
        Color tint,
        Vector2 lightUv,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d,
        Vector2 ua,
        Vector2 ub,
        Vector2 uc,
        Vector2 ud)
    {
        int start = vertices.Count;
        vertices.Add(block + a);
        vertices.Add(block + b);
        vertices.Add(block + c);
        vertices.Add(block + d);
        uvs.Add(ua);
        uvs.Add(ub);
        uvs.Add(uc);
        uvs.Add(ud);
        uv2.Add(lightUv);
        uv2.Add(lightUv);
        uv2.Add(lightUv);
        uv2.Add(lightUv);
        colors.Add(tint);
        colors.Add(tint);
        colors.Add(tint);
        colors.Add(tint);
        indices.Add(start);
        indices.Add(start + 1);
        indices.Add(start + 2);
        indices.Add(start);
        indices.Add(start + 2);
        indices.Add(start + 3);
    }

    static Color Lit(ushort id, int face, byte sky, byte blockLight, out float blockCurve)
    {
        float skyCurve = Mathf.Pow(Mathf.Clamp(sky / 15f, 0f, 1f), 1.25f);
        blockCurve = Mathf.Pow(Mathf.Clamp(blockLight / 15f, 0f, 1f), 1.25f);
        float faceShade = face switch
        {
            2 => 1f,
            3 => 0.62f,
            _ => 0.8f,
        };
        Color biome = Colors.White;
        if (id == BlockIds.Grass && face == 2)
        {
            biome = new Color(79f / 255f, 225f / 255f, 56f / 255f);
        }
        else if (id == BlockIds.Leaves || id == BlockIds.Plant)
        {
            biome = new Color(30f / 255f, 191f / 255f, 1f / 255f);
        }

        return new Color(biome.R * faceShade, biome.G * faceShade, biome.B * faceShade, skyCurve);
    }

    static Color Tint(ushort id, Vector3I normal)
    {
        if (id == BlockIds.Grass && normal.Y > 0)
        {
            return new Color(0.34f, 0.62f, 0.27f);
        }

        if (id == BlockIds.Grass || id == BlockIds.Dirt)
        {
            return new Color(0.46f, 0.33f, 0.18f);
        }

        if (id == BlockIds.Planks || id == BlockIds.Slab)
        {
            return new Color(0.64f, 0.5f, 0.28f);
        }

        if (id == BlockIds.Plant)
        {
            return new Color(0.3f, 0.62f, 0.24f);
        }

        if (id == BlockIds.Door || id == BlockIds.Trapdoor)
        {
            return new Color(0.45f, 0.3f, 0.16f);
        }

        if (id == BlockIds.Stairs || id == BlockIds.Fence)
        {
            return new Color(0.58f, 0.58f, 0.6f);
        }

        if (id == BlockIds.Torch)
        {
            return new Color(0.95f, 0.72f, 0.28f);
        }

        if (id == BlockIds.Carpet)
        {
            return new Color(0.55f, 0.18f, 0.16f);
        }

        if (id == BlockIds.Water)
        {
            return new Color(0.2f, 0.4f, 0.75f);
        }

        if (id == BlockIds.Magma)
        {
            return new Color(0.85f, 0.28f, 0.08f);
        }

        if (id == BlockIds.Chest)
        {
            return new Color(0.52f, 0.36f, 0.16f);
        }

        if (id == BlockIds.Log)
        {
            return new Color(0.42f, 0.28f, 0.14f);
        }

        if (id == BlockIds.Leaves)
        {
            return new Color(0.22f, 0.48f, 0.2f);
        }

        return new Color(0.55f, 0.55f, 0.58f);
    }
}
