using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Godot;

namespace SurvivalcraftGodot;

public readonly struct ModelMesh
{
    public Vector3[] Positions { get; init; }
    public Vector2[] Uvs { get; init; }
}

/// <summary>
/// 读取原版 Collada 方块模型。节点矩阵用列主序，贴图 V 翻到 Godot 的上原点。
/// </summary>
public static class BlockModel
{
    static readonly Dictionary<string, ModelMesh> Meshes = new();

    public static bool TryGet(string name, out ModelMesh mesh) => Meshes.TryGetValue(name, out mesh);

    public static void Load(string repositoryRoot)
    {
        Meshes.Clear();
        string dir = Path.Combine(repositoryRoot, "Survivalcraft", "Content", "Assets", "Models");
        if (!Directory.Exists(dir))
        {
            return;
        }

        ReadFile(Path.Combine(dir, "Torch.dae"));
        ReadFile(Path.Combine(dir, "Stairs.dae"));
        ReadFile(Path.Combine(dir, "Slab.dae"));
        ReadFile(Path.Combine(dir, "WoodenFence.dae"));
        ReadFile(Path.Combine(dir, "WoodenDoor.dae"));
        ReadFile(Path.Combine(dir, "WoodenTrapdoor.dae"));
        ReadFile(Path.Combine(dir, "Furnace.dae"));
        ReadFile(Path.Combine(dir, "Button.dae"));
    }

    static void ReadFile(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        string text = File.ReadAllText(path);
        var matrices = new Dictionary<string, float[]>();
        foreach (Match match in Regex.Matches(text, "<matrix sid=\"transform\">([^<]+)</matrix>\\s*<instance_geometry url=\"#([^\"]+)\""))
        {
            matrices[match.Groups[2].Value] = ParseFloats(match.Groups[1].Value);
        }

        foreach (Match geometry in Regex.Matches(text, "<geometry id=\"([^\"]+)\"[\\s\\S]*?</geometry>"))
        {
            string id = geometry.Groups[1].Value;
            string body = geometry.Value;
            Match positions = Regex.Match(body, "positions-array\"[^>]*>([^<]+)");
            Match uvs = Regex.Match(body, "map-array\"[^>]*>([^<]+)");
            Match indices = Regex.Match(body, "<p>([^<]+)</p>");
            if (!positions.Success || !uvs.Success || !indices.Success || !matrices.TryGetValue(id, out float[]? matrix))
            {
                continue;
            }

            float[] rawPositions = ParseFloats(positions.Groups[1].Value);
            float[] rawUvs = ParseFloats(uvs.Groups[1].Value);
            int[] corners = ParseInts(indices.Groups[1].Value);
            var outPos = new List<Vector3>();
            var outUv = new List<Vector2>();
            for (int i = 0; i + 2 < corners.Length; i += 3)
            {
                int positionIndex = corners[i];
                int uvIndex = corners[i + 2];
                if ((positionIndex + 1) * 3 > rawPositions.Length || (uvIndex + 1) * 2 > rawUvs.Length)
                {
                    continue;
                }

                var local = new Vector3(
                    rawPositions[positionIndex * 3],
                    rawPositions[positionIndex * 3 + 1],
                    rawPositions[positionIndex * 3 + 2]);
                outPos.Add(Transform(matrix, local));
                outUv.Add(new Vector2(rawUvs[uvIndex * 2], 1f - rawUvs[uvIndex * 2 + 1]));
            }

            string name = id.EndsWith("-mesh") ? id[..^5] : id;
            Meshes[name] = new ModelMesh { Positions = outPos.ToArray(), Uvs = outUv.ToArray() };
        }
    }

    /// 原版 Collada 按行写出矩阵。乘列向量后，火把棍子从格子底部向上长。
    static Vector3 Transform(float[] matrix, Vector3 point)
    {
        return new Vector3(
            matrix[0] * point.X + matrix[1] * point.Y + matrix[2] * point.Z + matrix[3],
            matrix[4] * point.X + matrix[5] * point.Y + matrix[6] * point.Z + matrix[7],
            matrix[8] * point.X + matrix[9] * point.Y + matrix[10] * point.Z + matrix[11]);
    }

    static float[] ParseFloats(string text)
    {
        string[] parts = text.Split((char[])null!, System.StringSplitOptions.RemoveEmptyEntries);
        var values = new float[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            values[i] = float.Parse(parts[i], CultureInfo.InvariantCulture);
        }

        return values;
    }

    static int[] ParseInts(string text)
    {
        string[] parts = text.Split((char[])null!, System.StringSplitOptions.RemoveEmptyEntries);
        var values = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            values[i] = int.Parse(parts[i], CultureInfo.InvariantCulture);
        }

        return values;
    }
}
