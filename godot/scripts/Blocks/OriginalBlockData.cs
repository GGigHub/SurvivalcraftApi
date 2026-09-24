using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace SurvivalcraftGodot;

public readonly struct OriginalBlockRow
{
    public string ClassName { get; init; }
    public int Index { get; init; }
    public ushort DropId { get; init; }
    public int DropCount { get; init; }
    public int TextureSlot { get; init; }
    public bool Collidable { get; init; }
    public bool PlacementTransparent { get; init; }
    public bool DiggingTransparent { get; init; }
    public float FuelHeat { get; init; }
    public float FuelTime { get; init; }
    public int Durability { get; init; }
}

public static class OriginalBlockData
{
    static readonly Dictionary<string, int> Indices = new();
    static readonly Dictionary<int, OriginalBlockRow> Rows = new();

    public static int Count => Rows.Count;

    public static bool Load(string repositoryRoot)
    {
        Indices.Clear();
        Rows.Clear();
        string blockDir = Path.Combine(repositoryRoot, "Survivalcraft", "Block");
        string dataPath = Path.Combine(repositoryRoot, "Survivalcraft", "Content", "Assets", "BlocksData.txt");
        if (!Directory.Exists(blockDir) || !File.Exists(dataPath))
        {
            return false;
        }

        foreach (string file in Directory.GetFiles(blockDir, "*.cs"))
        {
            string text = File.ReadAllText(file);
            Match classMatch = Regex.Match(text, @"class\s+(\w+)");
            Match indexMatch = Regex.Match(text, @"public\s+static\s+int\s+Index\s*=\s*(\d+)");
            if (!classMatch.Success || !indexMatch.Success)
            {
                continue;
            }

            Indices[classMatch.Groups[1].Value] = int.Parse(indexMatch.Groups[1].Value);
        }

        string[] lines = File.ReadAllLines(dataPath);
        if (lines.Length == 0)
        {
            return false;
        }

        string[] header = lines[0].Split(';');
        int dropColumn = Column(header, "DefaultDropContent");
        int countColumn = Column(header, "DefaultDropCount");
        int textureColumn = Column(header, "DefaultTextureSlot");
        int collidableColumn = Column(header, "IsCollidable");
        int placementColumn = Column(header, "IsPlacementTransparent");
        int diggingColumn = Column(header, "IsDiggingTransparent");
        int fuelHeatColumn = Column(header, "FuelHeatLevel");
        int fuelTimeColumn = Column(header, "FuelFireDuration");
        int durabilityColumn = Column(header, "Durability");
        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                continue;
            }

            string[] cells = lines[i].Split(';');
            string className = cells[0];
            if (string.IsNullOrEmpty(className) || !Indices.TryGetValue(className, out int index))
            {
                continue;
            }

            string dropText = Cell(cells, dropColumn);
            ushort dropId = (ushort)index;
            if (dropText.StartsWith('#'))
            {
                string target = dropText[1..];
                dropId = string.IsNullOrEmpty(target)
                    ? (ushort)index
                    : Indices.TryGetValue(target, out int targetIndex) ? (ushort)targetIndex : (ushort)0;
            }
            else if (string.IsNullOrEmpty(dropText))
            {
                dropId = BlockIds.Air;
            }

            int dropCount = 1;
            string countText = Cell(cells, countColumn);
            if (int.TryParse(countText, out int parsed))
            {
                dropCount = parsed;
            }

            int textureSlot = 0;
            int.TryParse(Cell(cells, textureColumn), out textureSlot);
            Rows[index] = new OriginalBlockRow
            {
                ClassName = className,
                Index = index,
                DropId = dropCount > 0 ? dropId : BlockIds.Air,
                DropCount = dropCount,
                TextureSlot = textureSlot,
                Collidable = Cell(cells, collidableColumn) == "TRUE",
                PlacementTransparent = Cell(cells, placementColumn) == "TRUE",
                DiggingTransparent = Cell(cells, diggingColumn) == "TRUE",
                FuelHeat = ParseFloat(Cell(cells, fuelHeatColumn)),
                FuelTime = ParseFloat(Cell(cells, fuelTimeColumn)),
                Durability = int.TryParse(Cell(cells, durabilityColumn), out int durability) ? durability : -1,
            };
        }

        BlockModel.Load(repositoryRoot);
        return Rows.Count > 0;
    }

    public static bool TryGet(int index, out OriginalBlockRow row) => Rows.TryGetValue(index, out row);

    public static int IndexOf(string className) => Indices.TryGetValue(className, out int index) ? index : -1;

    static int Column(string[] header, string name)
    {
        for (int i = 0; i < header.Length; i++)
        {
            if (header[i] == name)
            {
                return i;
            }
        }

        return -1;
    }

    static string Cell(string[] cells, int column) =>
        column >= 0 && column < cells.Length ? cells[column] : "";

    static float ParseFloat(string text) =>
        float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float value)
            ? value
            : 0f;
}
