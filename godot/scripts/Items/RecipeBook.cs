using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SurvivalcraftGodot;

public readonly struct CraftRecipe
{
    public int ResultId { get; init; }
    public int ResultCount { get; init; }
    public IReadOnlyDictionary<string, int> IngredientCounts { get; init; }
    public float RequiredHeatLevel { get; init; }
}

/// <summary>
/// 从原版 CraftingRecipes.xml 读入配方，材料名经方块 Index / CraftingId 映射为 id。
/// </summary>
public sealed class RecipeBook
{
    readonly List<CraftRecipe> _recipes;
    readonly Dictionary<string, int> _classToIndex;
    readonly Dictionary<string, List<int>> _craftingIdToIndices;
    readonly Dictionary<int, string> _indexToCraftingId;

    RecipeBook(
        List<CraftRecipe> recipes,
        Dictionary<string, int> classToIndex,
        Dictionary<string, List<int>> craftingIdToIndices,
        Dictionary<int, string> indexToCraftingId)
    {
        _recipes = recipes;
        _classToIndex = classToIndex;
        _craftingIdToIndices = craftingIdToIndices;
        _indexToCraftingId = indexToCraftingId;
    }

    public int Count => _recipes.Count;

    public IReadOnlyList<CraftRecipe> Recipes => _recipes;

    /// <param name="recipesXmlPath">CraftingRecipes.xml 路径</param>
    /// <param name="blockDirectory">Survivalcraft/Block 目录，用于扫 class 与 Index</param>
    /// <param name="blocksDataPath">BlocksData.txt，用于 CraftingId → 方块</param>
    public static RecipeBook Load(string recipesXmlPath, string blockDirectory, string blocksDataPath)
    {
        Dictionary<string, int> classToIndex = ScanBlockIndices(blockDirectory);
        LoadCraftingIds(blocksDataPath, classToIndex, out Dictionary<string, List<int>> craftingIdToIndices, out Dictionary<int, string> indexToCraftingId);
        var recipes = new List<CraftRecipe>();
        if (File.Exists(recipesXmlPath))
        {
            XDocument doc = XDocument.Load(recipesXmlPath);
            foreach (XElement element in doc.Descendants("Recipe"))
            {
                if (TryDecodeRecipe(element, classToIndex, out CraftRecipe recipe))
                {
                    recipes.Add(recipe);
                }
            }
        }

        return new RecipeBook(recipes, classToIndex, craftingIdToIndices, indexToCraftingId);
    }

    public bool TryGetBlockIndex(string className, out int index) =>
        _classToIndex.TryGetValue(className, out index);

    /// <summary>
    /// 将配方材料名（方块类名或 CraftingId，可带 :data）映射到一个原版 Index。
    /// </summary>
    public bool TryResolveMaterial(string materialName, out int blockId)
    {
        blockId = 0;
        if (string.IsNullOrWhiteSpace(materialName))
        {
            return false;
        }

        string name = materialName.Split(':')[0];
        if (_classToIndex.TryGetValue(name, out blockId))
        {
            return true;
        }

        if (_craftingIdToIndices.TryGetValue(name, out List<int>? ids) && ids.Count > 0)
        {
            blockId = ids[0];
            return true;
        }

        return false;
    }

    public bool TrySmelt(ushort inputId, float heat, out int resultId, out int resultCount)
    {
        resultId = 0;
        resultCount = 0;
        if (heat <= 0f || !_indexToCraftingId.TryGetValue(inputId, out string? craftingId) || string.IsNullOrEmpty(craftingId))
        {
            return false;
        }

        foreach (CraftRecipe recipe in _recipes)
        {
            if (recipe.RequiredHeatLevel <= 0f || recipe.RequiredHeatLevel > heat)
            {
                continue;
            }

            if (recipe.IngredientCounts.Count == 1
                && recipe.IngredientCounts.TryGetValue(craftingId, out int need)
                && need == 1)
            {
                resultId = recipe.ResultId;
                resultCount = recipe.ResultCount;
                return true;
            }
        }

        return false;
    }

    public bool TryCraftFromInventory(Inventory inventory, ushort preferredId)
    {
        Dictionary<string, int> have = InventoryCraftingCounts(inventory);
        string preferred = "";
        if (preferredId != BlockIds.Air && _indexToCraftingId.TryGetValue(preferredId, out string? foundId) && foundId != null)
        {
            preferred = foundId;
        }

        CraftRecipe chosen = default;
        bool found = false;
        foreach (CraftRecipe recipe in _recipes)
        {
            if (!IsSubset(recipe.IngredientCounts, have))
            {
                continue;
            }

            if (preferred.Length > 0 && recipe.IngredientCounts.ContainsKey(preferred))
            {
                chosen = recipe;
                found = true;
                break;
            }

            if (!found)
            {
                chosen = recipe;
                found = true;
            }
        }

        if (!found)
        {
            return false;
        }

        foreach ((string name, int count) in chosen.IngredientCounts)
        {
            int left = count;
            for (int i = 0; i < Inventory.SlotCount && left > 0; i++)
            {
                if (inventory.Counts[i] <= 0 || !_indexToCraftingId.TryGetValue(inventory.Ids[i], out string? craftingId) || craftingId != name)
                {
                    continue;
                }

                while (left > 0 && inventory.Counts[i] > 0)
                {
                    inventory.TryConsume(i);
                    left--;
                }
            }
        }

        inventory.Add((ushort)chosen.ResultId, chosen.ResultCount);
        return true;
    }

    Dictionary<string, int> InventoryCraftingCounts(Inventory inventory)
    {
        var have = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < Inventory.SlotCount; i++)
        {
            if (inventory.Counts[i] <= 0)
            {
                continue;
            }

            if (!_indexToCraftingId.TryGetValue(inventory.Ids[i], out string? craftingId) || string.IsNullOrEmpty(craftingId))
            {
                continue;
            }

            have[craftingId] = have.TryGetValue(craftingId, out int existing) ? existing + inventory.Counts[i] : inventory.Counts[i];
        }

        return have;
    }

    static bool IsSubset(IReadOnlyDictionary<string, int> need, Dictionary<string, int> have)
    {
        foreach ((string key, int count) in need)
        {
            if (!have.TryGetValue(key, out int owned) || owned < count)
            {
                return false;
            }
        }

        return need.Count > 0;
    }

    /// <summary>
    /// 按材料多集（方块 id → 数量）查找匹配配方的产物。
    /// </summary>
    public bool TryFindByMaterials(IReadOnlyDictionary<int, int> materialCounts, out int resultId, out int resultCount)
    {
        resultId = 0;
        resultCount = 0;
        Dictionary<string, int> want = ToCraftingMultiset(materialCounts);
        if (want.Count == 0)
        {
            return false;
        }

        foreach (CraftRecipe recipe in _recipes)
        {
            if (MultisetEquals(recipe.IngredientCounts, want))
            {
                resultId = recipe.ResultId;
                resultCount = recipe.ResultCount;
                return true;
            }
        }

        return false;
    }

    Dictionary<string, int> ToCraftingMultiset(IReadOnlyDictionary<int, int> materialCounts)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach ((int id, int count) in materialCounts)
        {
            if (count <= 0)
            {
                continue;
            }

            if (!_indexToCraftingId.TryGetValue(id, out string? craftingId) || string.IsNullOrEmpty(craftingId))
            {
                continue;
            }

            result[craftingId] = result.TryGetValue(craftingId, out int existing) ? existing + count : count;
        }

        return result;
    }

    static bool MultisetEquals(IReadOnlyDictionary<string, int> a, IReadOnlyDictionary<string, int> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        foreach ((string key, int count) in a)
        {
            if (!b.TryGetValue(key, out int other) || other != count)
            {
                return false;
            }
        }

        return true;
    }

    static Dictionary<string, int> ScanBlockIndices(string blockDirectory)
    {
        var indices = new Dictionary<string, int>(StringComparer.Ordinal);
        if (!Directory.Exists(blockDirectory))
        {
            return indices;
        }

        foreach (string file in Directory.GetFiles(blockDirectory, "*.cs"))
        {
            string text = File.ReadAllText(file);
            Match classMatch = Regex.Match(text, @"class\s+(\w+)");
            Match indexMatch = Regex.Match(text, @"public\s+static\s+int\s+Index\s*=\s*(\d+)");
            if (!classMatch.Success || !indexMatch.Success)
            {
                continue;
            }

            indices[classMatch.Groups[1].Value] = int.Parse(indexMatch.Groups[1].Value);
        }

        return indices;
    }

    static void LoadCraftingIds(
        string blocksDataPath,
        Dictionary<string, int> classToIndex,
        out Dictionary<string, List<int>> craftingIdToIndices,
        out Dictionary<int, string> indexToCraftingId)
    {
        craftingIdToIndices = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        indexToCraftingId = new Dictionary<int, string>();
        if (!File.Exists(blocksDataPath))
        {
            return;
        }

        string[] lines = File.ReadAllLines(blocksDataPath);
        if (lines.Length == 0)
        {
            return;
        }

        string[] header = lines[0].Split(';');
        int craftingColumn = -1;
        for (int i = 0; i < header.Length; i++)
        {
            if (header[i] == "CraftingId")
            {
                craftingColumn = i;
                break;
            }
        }

        if (craftingColumn < 0)
        {
            return;
        }

        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                continue;
            }

            string[] cells = lines[i].Split(';');
            if (cells.Length == 0)
            {
                continue;
            }

            string className = cells[0];
            if (string.IsNullOrEmpty(className) || !classToIndex.TryGetValue(className, out int index))
            {
                continue;
            }

            string craftingId = craftingColumn < cells.Length ? cells[craftingColumn] : "";
            if (string.IsNullOrEmpty(craftingId))
            {
                continue;
            }

            if (!craftingIdToIndices.TryGetValue(craftingId, out List<int>? list))
            {
                list = new List<int>();
                craftingIdToIndices[craftingId] = list;
            }

            list.Add(index);
            indexToCraftingId[index] = craftingId;
        }
    }

    static bool TryDecodeRecipe(XElement element, Dictionary<string, int> classToIndex, out CraftRecipe recipe)
    {
        recipe = default;
        XAttribute? resultAttr = element.Attribute("Result");
        if (resultAttr == null || string.IsNullOrEmpty(resultAttr.Value))
        {
            return false;
        }

        string resultName = resultAttr.Value.Split(':')[0];
        if (!classToIndex.TryGetValue(resultName, out int resultId))
        {
            return false;
        }

        int resultCount = 1;
        XAttribute? countAttr = element.Attribute("ResultCount");
        if (countAttr != null)
        {
            int.TryParse(countAttr.Value, out resultCount);
        }

        var letterToIngredient = new Dictionary<char, string>();
        foreach (XAttribute attr in element.Attributes())
        {
            string local = attr.Name.LocalName;
            if (local.Length == 1 && char.IsLower(local[0]))
            {
                letterToIngredient[local[0]] = attr.Value;
            }
        }

        var ingredientCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        string body = element.Value;
        foreach (string line in body.Split('\n'))
        {
            int first = line.IndexOf('"');
            int last = line.LastIndexOf('"');
            if (first < 0 || last <= first)
            {
                continue;
            }

            string pattern = line.Substring(first + 1, last - first - 1);
            foreach (char c in pattern)
            {
                if (!char.IsLower(c) || !letterToIngredient.TryGetValue(c, out string? ingredient))
                {
                    continue;
                }

                string craftingId = ingredient.Split(':')[0];
                ingredientCounts[craftingId] = ingredientCounts.TryGetValue(craftingId, out int n) ? n + 1 : 1;
            }
        }

        if (ingredientCounts.Count == 0)
        {
            return false;
        }

        float heat = 0f;
        XAttribute? heatAttr = element.Attribute("RequiredHeatLevel");
        if (heatAttr != null)
        {
            float.TryParse(heatAttr.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out heat);
        }

        recipe = new CraftRecipe
        {
            ResultId = resultId,
            ResultCount = resultCount,
            IngredientCounts = ingredientCounts,
            RequiredHeatLevel = heat,
        };
        return true;
    }
}
