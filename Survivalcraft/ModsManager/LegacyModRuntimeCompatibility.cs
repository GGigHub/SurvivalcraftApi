using System.Collections;
using System.Reflection;
using System.Xml.Linq;
using Engine;
using XmlUtilities;

namespace Game {
    internal static class LegacyModRuntimeCompatibility {
        const BindingFlags StaticFieldFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        const BindingFlags InstanceFieldFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        const int Firearms28ExpectedGunsDataCount = 21;
        const int Firearms28ExpectedRecipeCount = 51;
        static readonly HashSet<Assembly> FirearmsMapInitializedAssemblies = [];

        internal static void InitializeBeforeBlocks() {
            foreach ((Assembly assembly, HashSet<string> breakIds) in
                     EnumerateCompatibilityAssemblies()) {
                if (breakIds.Contains(
                        LegacyModAssemblyCompatibility.FirearmsResetLightsaberColorsBreakId)) {
                    ResetFirearms28LightsaberColors(assembly);
                }
                if (breakIds.Contains(
                        LegacyModAssemblyCompatibility.FirearmsResetAnimationStepsBreakId)) {
                    ResetFirearms28AnimationSteps(assembly);
                }
                if (breakIds.Contains(
                        LegacyModAssemblyCompatibility.FirearmsHostGunsDataBreakId)) {
                    InitializeFirearms28GunsData(assembly);
                }
            }
        }

        internal static void InitializeAfterBlocks() {
            foreach ((Assembly assembly, HashSet<string> breakIds) in
                     EnumerateCompatibilityAssemblies()) {
                if (breakIds.Contains(
                        LegacyModAssemblyCompatibility.FirearmsHostRecipesBreakId)) {
                    InitializeFirearms28Recipes(assembly);
                }
                if (breakIds.Contains(
                        LegacyModAssemblyCompatibility.FirearmsHostMapInitializationBreakId)) {
                    InitializeFirearms28MapOnce(assembly);
                }
            }
        }

        internal static void UpdateAfterLegacyEntryPoint(IUpdateable updateable, float dt) {
            if (updateable is not SubsystemProjectiles projectiles
                || !HasCompatibilityBreak(
                    updateable.GetType().Assembly,
                    LegacyModAssemblyCompatibility.FirearmsHostProjectileUpdateBreakId)) {
                return;
            }
            projectiles.UpdateProjectilesCore(dt);
        }

        internal static int ClearStaticDictionary(
            Assembly assembly,
            string typeName,
            string fieldName) {
            ArgumentNullException.ThrowIfNull(assembly);
            IDictionary dictionary = RequireStaticField(
                    RequireType(assembly, typeName),
                    fieldName)
                .GetValue(null) as IDictionary
                ?? throw new InvalidOperationException(
                    $"Legacy field '{typeName}.{fieldName}' is not a dictionary.");
            int previousCount = dictionary.Count;
            dictionary.Clear();
            return previousCount;
        }

        internal static int RemapLegacyBlockValue(
            int value,
            IReadOnlyDictionary<int, int> blockIndexMap) {
            ArgumentNullException.ThrowIfNull(blockIndexMap);
            int historicalContents = Terrain.ExtractContents(value);
            return blockIndexMap.TryGetValue(historicalContents, out int currentContents)
                ? Terrain.ReplaceContents(value, currentContents)
                : value;
        }

        internal static int RequireLegacyBlockIndex(
            IReadOnlyDictionary<int, int> blockIndexMap,
            int historicalIndex,
            string usage) {
            ArgumentNullException.ThrowIfNull(blockIndexMap);
            if (!blockIndexMap.TryGetValue(historicalIndex, out int currentIndex)) {
                throw new InvalidOperationException(
                    $"Legacy block index {historicalIndex} for {usage} has no current allocation.");
            }
            return currentIndex;
        }

        static IEnumerable<KeyValuePair<Assembly, HashSet<string>>>
            EnumerateCompatibilityAssemblies() {
            HashSet<Assembly> visited = [];
            foreach (ModEntity modEntity in ModsManager.ModList) {
                foreach ((Assembly assembly, HashSet<string> breakIds) in
                         modEntity.LegacyCompatibilityBreakIdsByAssembly) {
                    if (visited.Add(assembly)) {
                        yield return new KeyValuePair<Assembly, HashSet<string>>(
                            assembly,
                            breakIds);
                    }
                }
            }
        }

        static bool HasCompatibilityBreak(Assembly assembly, string breakId) {
            foreach (ModEntity modEntity in ModsManager.ModList) {
                if (modEntity.HasLegacyCompatibilityBreak(assembly, breakId)) {
                    return true;
                }
            }
            return false;
        }

        static void InitializeFirearms28GunsData(Assembly assembly) {
            Type functionsType = RequireType(assembly, "Game.Functions");
            Type gunsDataType = RequireType(assembly, "Game.GunsData");
            Type projectileBlockType = RequireType(assembly, "Game.GMProjectileBlock");
            Dictionary<int, int> blockIndexMap = BuildFirearms28BlockIndexMap(assembly);
            SetSiyahAkrepLoaded(assembly, false);

            IList dataList = RequireStaticList(functionsType, "dataList");
            dataList.Clear();
            Array projectiles = RequireStaticField(projectileBlockType, "Projectiles")
                .GetValue(null) as Array
                ?? throw new InvalidOperationException(
                    "Firearms 2.8 projectile table is unavailable.");
            Dictionary<string, FieldInfo> dataFields = GetInstanceFields(gunsDataType);
            XElement root = ContentManager.Get<XElement>("Guns")
                ?? throw new InvalidOperationException(
                    "Firearms 2.8 Guns.xml content is unavailable.");

            int sourceCount = 0;
            foreach (XElement element in root.Elements()) {
                if (element.Name.LocalName != "GunsData") {
                    continue;
                }
                sourceCount++;
#pragma warning disable IL2067
                object gunsData = Activator.CreateInstance(gunsDataType)
                    ?? throw new InvalidOperationException(
                        "Could not create a Firearms 2.8 GunsData instance.");
#pragma warning restore IL2067

                int historicalIndex = XmlUtils.GetAttributeValue<int>(element, "Index");
                int index = RequireLegacyBlockIndex(
                    blockIndexMap,
                    historicalIndex,
                    "Firearms 2.8 gun data");
                int bulletPower = XmlUtils.GetAttributeValue(element, "BulletPower", 0);
                int bulletValue = XmlUtils.GetAttributeValue(element, "BulletValue", 603);
                float range = 25f;
                float armorPiercing = 0.2f;
                float explosionPressure;

                int historicalBulletContents = Terrain.ExtractContents(bulletValue);
                int bulletContents = historicalBulletContents == 603
                    ? RequireLegacyBlockIndex(
                        blockIndexMap,
                        historicalBulletContents,
                        "Firearms 2.8 projectile value")
                    : Terrain.ExtractContents(
                        RemapLegacyBlockValue(bulletValue, blockIndexMap));
                if (bulletContents < 0 || bulletContents >= BlocksManager.Blocks.Length) {
                    throw new InvalidOperationException(
                        $"Firearms 2.8 bullet content index {bulletContents} is outside the block table.");
                }
                if (historicalBulletContents == 603) {
                    if (BlocksManager.Blocks[bulletContents].GetType() != projectileBlockType) {
                        throw new InvalidOperationException(
                            $"Firearms 2.8 projectile index 603 resolved to "
                            + $"'{BlocksManager.Blocks[bulletContents].GetType().FullName}' instead of "
                            + $"'{projectileBlockType.FullName}'.");
                    }
                    int projectileData = Terrain.ExtractData(bulletValue);
                    int projectileIndex = (projectileData - projectileData % 1000) / 1000;
                    if (bulletPower is < 0 or > 999) {
                        bulletPower = 0;
                    }
                    if (bulletPower > 0) {
                        int rewrittenData = bulletPower
                            + projectileData
                            - projectileData % 1000;
                        bulletValue = Terrain.ReplaceData(bulletValue, rewrittenData);
                    }
                    object projectile = RequireArrayElement(
                        projectiles,
                        projectileIndex,
                        "Firearms 2.8 projectile");
                    Type projectileType = projectile.GetType();
                    range = GetInstanceFieldValue<float>(
                        projectileType,
                        projectile,
                        "Range");
                    armorPiercing = GetInstanceFieldValue<float>(
                        projectileType,
                        projectile,
                        "ArmorPiecing");
                    if (bulletPower <= 0) {
                        bulletPower = (int)GetInstanceFieldValue<float>(
                            projectileType,
                            projectile,
                            "ProjectilePower");
                    }
                    explosionPressure = GetInstanceFieldValue<float>(
                        projectileType,
                        projectile,
                        "ExplosionPressure");
                }
                else {
                    Block bulletBlock = BlocksManager.Blocks[bulletContents];
                    int currentBulletValue = Terrain.ReplaceContents(
                        bulletValue,
                        bulletContents);
                    bulletPower = (int)bulletBlock.GetProjectilePower(currentBulletValue);
                    explosionPressure = bulletBlock.GetExplosionPressure(currentBulletValue);
                }
                bulletValue = Terrain.ReplaceContents(bulletValue, bulletContents);

                SetField(dataFields, gunsData, "Index", index);
                SetField(
                    dataFields,
                    gunsData,
                    "Name",
                    XmlUtils.GetAttributeValue(element, "Name", string.Empty));
                SetField(dataFields, gunsData, "BulletValue", bulletValue);
                SetField(dataFields, gunsData, "BulletPower", bulletPower);
                SetField(dataFields, gunsData, "BulletExplosionPressure", explosionPressure);
                SetField(
                    dataFields,
                    gunsData,
                    "MaxBulletCount",
                    XmlUtils.GetAttributeValue<int>(element, "MaxBulletCount"));
                SetField(
                    dataFields,
                    gunsData,
                    "MagazineData",
                    XmlUtils.GetAttributeValue<int>(element, "MagazineData"));
                SetField(
                    dataFields,
                    gunsData,
                    "FireInterval",
                    XmlUtils.GetAttributeValue<float>(element, "FireInterval"));
                SetField(
                    dataFields,
                    gunsData,
                    "Cluster",
                    XmlUtils.GetAttributeValue<int>(element, "Cluster"));
                SetField(
                    dataFields,
                    gunsData,
                    "Deviation",
                    XmlUtils.GetAttributeValue<float>(element, "Deviation"));
                SetField(
                    dataFields,
                    gunsData,
                    "FireParticleOffset",
                    XmlUtils.GetAttributeValue<Vector3>(element, "FireParticleOffset"));
                SetField(
                    dataFields,
                    gunsData,
                    "FireAudio",
                    XmlUtils.GetAttributeValue<string>(element, "FireAudio"));
                SetField(
                    dataFields,
                    gunsData,
                    "ShakeWhenFire",
                    XmlUtils.GetAttributeValue<bool>(element, "ShakeWhenFire"));
                SetField(
                    dataFields,
                    gunsData,
                    "DamageRate",
                    XmlUtils.GetAttributeValue<float>(element, "DamageRate"));
                SetField(
                    dataFields,
                    gunsData,
                    "SightsType",
                    XmlUtils.GetAttributeValue<string>(element, "SightsType"));
                SetField(
                    dataFields,
                    gunsData,
                    "Weight",
                    XmlUtils.GetAttributeValue<float>(element, "Weight"));
                SetField(
                    dataFields,
                    gunsData,
                    "Components",
                    XmlUtils.GetAttributeValue<string>(element, "Components"));
                SetField(
                    dataFields,
                    gunsData,
                    "Recoil",
                    XmlUtils.GetAttributeValue<Vector2>(element, "Recoil"));
                SetField(
                    dataFields,
                    gunsData,
                    "Level",
                    XmlUtils.GetAttributeValue<int>(element, "Level"));
                SetField(
                    dataFields,
                    gunsData,
                    "Category",
                    XmlUtils.GetAttributeValue<string>(element, "Category"));
                SetField(
                    dataFields,
                    gunsData,
                    "IsDraftVisible",
                    XmlUtils.GetAttributeValue(element, "IsDraftVisible", true));
                SetField(dataFields, gunsData, "ReloadDuration", 0f);
                SetField(
                    dataFields,
                    gunsData,
                    "FireAudioVolume",
                    historicalIndex switch {
                        629 => 2.5f,
                        533 => 1.5f,
                        _ => 1f
                    });
                SetField(dataFields, gunsData, "Range", range);
                SetField(dataFields, gunsData, "ArmorPiercing", armorPiercing);
                if (index > 0) {
                    dataList.Add(gunsData);
                }
            }

            if (sourceCount != Firearms28ExpectedGunsDataCount
                || dataList.Count != Firearms28ExpectedGunsDataCount) {
                throw new InvalidOperationException(
                    $"Firearms 2.8 Guns.xml produced {dataList.Count} usable entries from "
                    + $"{sourceCount}; expected {Firearms28ExpectedGunsDataCount}.");
            }
            Log.Information(
                $"[Firearms 2.8 compatibility] Initialized {dataList.Count} gun data entries "
                + $"using {blockIndexMap.Count} historical block mappings.");
        }

        static Dictionary<int, int> BuildFirearms28BlockIndexMap(Assembly assembly) {
            Dictionary<int, int> result = [];
            foreach ((Type blockType, int historicalIndex) in
                     BlocksManager.BlockTypeToOriginalIndex) {
                if (blockType.Assembly != assembly || historicalIndex <= 0) {
                    continue;
                }
                if (!BlocksManager.BlockTypeToIndex.TryGetValue(
                        blockType,
                        out int currentIndex)) {
                    throw new InvalidOperationException(
                        $"Firearms 2.8 block '{blockType.FullName}' has no current allocation.");
                }
                if (currentIndex <= 0
                    || currentIndex >= BlocksManager.Blocks.Length
                    || BlocksManager.Blocks[currentIndex].GetType() != blockType) {
                    throw new InvalidOperationException(
                        $"Firearms 2.8 block '{blockType.FullName}' has invalid current index "
                        + $"{currentIndex} for historical index {historicalIndex}.");
                }
                if (!result.TryAdd(historicalIndex, currentIndex)) {
                    throw new InvalidOperationException(
                        $"Firearms 2.8 historical block index {historicalIndex} is ambiguous.");
                }
            }
            if (result.Count == 0) {
                throw new InvalidOperationException(
                    "Firearms 2.8 has no historical-to-current block index mappings.");
            }
            return result;
        }

        static void ResetFirearms28LightsaberColors(Assembly assembly) {
            int previousCount = ClearStaticDictionary(
                assembly,
                "Game.Lightsaber",
                "Colors");
            if (previousCount > 0) {
                Log.Information(
                    $"[Firearms 2.8 compatibility] Reset {previousCount} lightsaber colors "
                    + "before repeated block initialization.");
            }
        }

        static void ResetFirearms28AnimationSteps(Assembly assembly) {
            int previousCount = ClearStaticDictionary(
                assembly,
                "Game.Functions",
                "AllSteps");
            if (previousCount > 0) {
                Log.Information(
                    $"[Firearms 2.8 compatibility] Reset {previousCount} animation steps "
                    + "before repeated block initialization.");
            }
        }

        static void InitializeFirearms28Recipes(Assembly assembly) {
            Type managerType = RequireType(assembly, "Game.FirearmsRecipesManager");
            Type recipeType = RequireType(assembly, "Game.FirearmsRecipe");
            SetSiyahAkrepLoaded(assembly, false);

            IList recipes = RequireStaticList(managerType, "m_recipes");
            recipes.Clear();
            Dictionary<string, FieldInfo> recipeFields = GetInstanceFields(recipeType);
            XElement root = ContentManager.Get<XElement>("FirearmsRecipes")
                ?? throw new InvalidOperationException(
                    "Firearms 2.8 FirearmsRecipes.xml content is unavailable.");

            int sourceCount = 0;
            foreach (XElement element in root.Descendants("Recipe")) {
                sourceCount++;
#pragma warning disable IL2067
                object recipe = Activator.CreateInstance(recipeType)
                    ?? throw new InvalidOperationException(
                        "Could not create a Firearms 2.8 FirearmsRecipe instance.");
#pragma warning restore IL2067

                SetField(
                    recipeFields,
                    recipe,
                    "IsMachineTool",
                    XmlUtils.GetAttributeValue(element, "IsMachineTool", false));
                SetField(
                    recipeFields,
                    recipe,
                    "Category",
                    XmlUtils.GetAttributeValue(element, "Category", "杂项"));
                SetField(
                    recipeFields,
                    recipe,
                    "OnlyShowInSA",
                    XmlUtils.GetAttributeValue(element, "OnlyShowInSA", false));
                SetField(
                    recipeFields,
                    recipe,
                    "ResultValue",
                    CraftingRecipesManager.DecodeResult(
                        XmlUtils.GetAttributeValue<string>(element, "Result")));
                SetField(
                    recipeFields,
                    recipe,
                    "ResultCount",
                    XmlUtils.GetAttributeValue<int>(element, "ResultCount"));

                string remains = XmlUtils.GetAttributeValue(element, "Remains", string.Empty);
                if (!string.IsNullOrEmpty(remains)) {
                    SetField(
                        recipeFields,
                        recipe,
                        "RemainsValue",
                        CraftingRecipesManager.DecodeResult(remains));
                    SetField(
                        recipeFields,
                        recipe,
                        "RemainsCount",
                        XmlUtils.GetAttributeValue<int>(element, "RemainsCount"));
                }
                SetField(
                    recipeFields,
                    recipe,
                    "RequiredPlayerLevel",
                    XmlUtils.GetAttributeValue(element, "RequiredPlayerLevel", 1f));

                Dictionary<char, string> ingredientsBySymbol = [];
                foreach (XAttribute attribute in element.Attributes().Where(attribute =>
                             attribute.Name.LocalName.Length == 1
                             && char.IsLower(attribute.Name.LocalName[0]))) {
                    string ingredient = attribute.Value;
                    if (ingredient.Contains("sarod", StringComparison.Ordinal)) {
                        ingredient = BlocksManager.Blocks[195].CraftingId;
                    }
                    CraftingRecipesManager.DecodeIngredient(
                        ingredient,
                        out string craftingId,
                        out int? data);
                    if (BlocksManager.FindBlocksByCraftingId(craftingId).Length == 0) {
                        throw new InvalidOperationException(
                            $"Firearms 2.8 recipe ingredient '{ingredient}' was not found.");
                    }
                    if (data is < 0 or > 262143) {
                        throw new InvalidOperationException(
                            $"Firearms 2.8 recipe ingredient '{ingredient}' has invalid data.");
                    }
                    ingredientsBySymbol.Add(attribute.Name.LocalName[0], ingredient);
                }

                string[] ingredients = GetField(recipeFields, "Ingredients")
                    .GetValue(recipe) as string[]
                    ?? throw new InvalidOperationException(
                        "Firearms 2.8 recipe ingredient array is unavailable.");
                string[] rows = element.Value.Trim().Split(['\n'], StringSplitOptions.None);
                for (int row = 0; row < rows.Length; row++) {
                    int firstQuote = rows[row].IndexOf('"');
                    int lastQuote = rows[row].LastIndexOf('"');
                    if (firstQuote < 0 || lastQuote <= firstQuote) {
                        throw new InvalidOperationException(
                            "Firearms 2.8 recipe contains an invalid row.");
                    }
                    string pattern = rows[row].Substring(
                        firstQuote + 1,
                        lastQuote - firstQuote - 1);
                    for (int column = 0; column < pattern.Length; column++) {
                        char symbol = pattern[column];
                        if (!char.IsLower(symbol)) {
                            continue;
                        }
                        int ingredientIndex = column + row * 4;
                        if (ingredientIndex >= ingredients.Length) {
                            throw new InvalidOperationException(
                                "Firearms 2.8 recipe exceeds its 4x4 ingredient grid.");
                        }
                        ingredients[ingredientIndex] = ingredientsBySymbol[symbol];
                    }
                }
                recipes.Add(recipe);
            }

            if (sourceCount != Firearms28ExpectedRecipeCount
                || recipes.Count != Firearms28ExpectedRecipeCount) {
                throw new InvalidOperationException(
                    $"Firearms 2.8 recipes produced {recipes.Count} entries from "
                    + $"{sourceCount}; expected {Firearms28ExpectedRecipeCount}.");
            }
            Log.Information(
                $"[Firearms 2.8 compatibility] Initialized {recipes.Count} recipes.");
        }

        static void InitializeFirearms28MapOnce(Assembly assembly) {
            lock (FirearmsMapInitializedAssemblies) {
                if (!FirearmsMapInitializedAssemblies.Add(assembly)) {
                    return;
                }
            }
            try {
                Type mapManagerType = RequireType(assembly, "Game.MapManager");
                MethodInfo initialize = mapManagerType.GetMethod(
                    "Initialize",
                    StaticFieldFlags,
                    binder: null,
                    types: Type.EmptyTypes,
                    modifiers: null)
                    ?? throw new MissingMethodException(
                        mapManagerType.FullName,
                        "Initialize()");
                initialize.Invoke(null, null);
                Log.Information(
                    "[Firearms 2.8 compatibility] Initialized map settings once.");
            }
            catch {
                lock (FirearmsMapInitializedAssemblies) {
                    FirearmsMapInitializedAssemblies.Remove(assembly);
                }
                throw;
            }
        }

        static void SetSiyahAkrepLoaded(Assembly assembly, bool value) {
            Type settingsType = RequireType(assembly, "Game.GMSettingManager");
            RequireStaticField(settingsType, "SALoaded").SetValue(null, value);
        }

        static Type RequireType(Assembly assembly, string name) =>
            assembly.GetType(name, throwOnError: true, ignoreCase: false)
            ?? throw new TypeLoadException(name);

        static FieldInfo RequireStaticField(Type type, string name) =>
            type.GetField(name, StaticFieldFlags)
            ?? throw new MissingFieldException(type.FullName, name);

        static IList RequireStaticList(Type type, string name) =>
            RequireStaticField(type, name).GetValue(null) as IList
            ?? throw new InvalidOperationException(
                $"Legacy field '{type.FullName}.{name}' is not a list.");

        static Dictionary<string, FieldInfo> GetInstanceFields(Type type) =>
            type.GetFields(InstanceFieldFlags).ToDictionary(
                field => field.Name,
                StringComparer.Ordinal);

        static FieldInfo GetField(
            IReadOnlyDictionary<string, FieldInfo> fields,
            string name) => fields.TryGetValue(name, out FieldInfo field)
                ? field
                : throw new MissingFieldException(name);

        static void SetField(
            IReadOnlyDictionary<string, FieldInfo> fields,
            object instance,
            string name,
            object value) => GetField(fields, name).SetValue(instance, value);

        static object RequireArrayElement(Array array, int index, string description) {
            if (index < 0 || index >= array.Length) {
                throw new InvalidOperationException(
                    $"{description} index {index} is outside a table of {array.Length} entries.");
            }
            return array.GetValue(index)
                ?? throw new InvalidOperationException(
                    $"{description} index {index} is empty.");
        }

        static T GetInstanceFieldValue<T>(Type type, object instance, string name) {
            FieldInfo field = type.GetField(name, InstanceFieldFlags)
                ?? throw new MissingFieldException(type.FullName, name);
            object value = field.GetValue(instance)
                ?? throw new InvalidOperationException(
                    $"Legacy field '{type.FullName}.{name}' is null.");
            return (T)value;
        }
    }
}
