using System.Collections.Generic;
using System.IO;

namespace SurvivalcraftGodot;

public static class CraftChecks
{
    public static List<string> Run(string repositoryRoot)
    {
        var failures = new List<string>();
        string recipesXml = Path.Combine(repositoryRoot, "Survivalcraft", "Content", "Assets", "CraftingRecipes.xml");
        string blockDir = Path.Combine(repositoryRoot, "Survivalcraft", "Block");
        string blocksData = Path.Combine(repositoryRoot, "Survivalcraft", "Content", "Assets", "BlocksData.txt");

        if (!File.Exists(recipesXml))
        {
            failures.Add("找不到 CraftingRecipes.xml");
            return failures;
        }

        RecipeBook book = RecipeBook.Load(recipesXml, blockDir, blocksData);
        if (book.Count <= 20)
        {
            failures.Add("配方数量大于20");
        }

        if (!book.TryGetBlockIndex("OakWoodBlock", out int woodId) ||
            !book.TryGetBlockIndex("PlanksBlock", out int planksId))
        {
            failures.Add("原版木头或木板 Index");
            return failures;
        }

        bool matched = book.TryFindByMaterials(
            new Dictionary<int, int> { [woodId] = 1 },
            out int resultId,
            out int resultCount);

        bool woodRecipeOk = matched && resultCount > 0 && (resultId == planksId || resultId == woodId);
        if (!woodRecipeOk)
        {
            failures.Add("木头相关配方匹配木板或木头产物");
        }

        return failures;
    }
}
