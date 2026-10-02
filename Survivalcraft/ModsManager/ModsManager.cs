// Game.ModsManager

using System.Collections.Frozen;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Engine;
using Engine.Graphics;
using Engine.Serialization;
using Game;
using Game.IContentReader;
using NuGet.Versioning;
using XmlUtilities;
using ZipArchive = Game.ZipArchive;
public static class ModsManager {
    public static string ModSuffix = ".scmod";
    public static string APIVersionString = "1.9.3.1";
    public static string ShortAPIVersionString = "1.9";
    public static NuGetVersion APINuGetVersion = new(1, 9, 3, 1);
    public static string GameVersion = "2.4.0.0";
    public static string ShortGameVersion = "2.4";
    public static string ReportLink = "https://gitee.com/SC-SPM/SurvivalcraftApi/issues";
    public static string APILatestReleaseLink_API = "https://gitee.com/api/v5/repos/SC-SPM/SurvivalcraftApi/releases/latest";
    public static string APILatestReleaseLink_Client = "https://gitee.com/SC-SPM/SurvivalcraftApi/releases/latest";
    public static string APIReleasesLink_API = "https://gitee.com/api/v5/repos/SC-SPM/SurvivalcraftApi/releases";
    public static string APIReleasesLink_Client = "https://gitee.com/SC-SPM/SurvivalcraftApi/releases/";
    public static string fName = "ModsManager";

    [Obsolete("使用ApiVersionString")]
    public enum ApiVersionEnum //不准确，弃用
    {
        Version15x = 3,
        Version170 = 17,
        Version180 = 18
    }

    [Obsolete("使用ApiVersionString")] public const ApiVersionEnum ApiVersion = ApiVersionEnum.Version180;

#if !ANDROID
    public static string ExternalPath => "app:";
    public static string DocPath = "app:/doc";
    public static string WorldsDirectoryName = $"{DocPath}/Worlds";
#endif
#if ANDROID
    public static string ExternalPath => EngineActivity.BasePath;
    public static string DocPath = EngineActivity.BasePath;
    public static string WorldsDirectoryName = $"{ExternalPath}/Worlds";
#endif
    public static string ProcessModListPath = $"{ExternalPath}/ProcessModLists";

    public static string ScreenCapturePath { get; } = $"{ExternalPath}/ScreenCapture";

    public static string UserDataPath { get; } = $"{DocPath}/UserId.dat";
    public static string CharacterSkinsDirectoryName { get; } = $"{DocPath}/CharacterSkins";
    public static string FurniturePacksDirectoryName { get; } = $"{DocPath}/FurniturePacks";

    public static string BlockTexturesDirectoryName { get; } = $"{DocPath}/TexturePacks";
    public static string CommunityContentCachePath { get; } = $"{DocPath}/CommunityContentCache.xml";
    public static string OriginalCommunityContentCachePath { get; } = $"{DocPath}/OriginalCommunityContentCache.xml";
    public static string ModsSettingsPath { get; } = $"{DocPath}/ModSettings.xml";
    public static string SettingPath { get; } = $"{DocPath}/Settings.xml";
    public static string ConfigsPath { get; } = $"{DocPath}/Configs.xml";
    public static string LogPath { get; } = $"{ExternalPath}/Bugs";
    public static string ModsPath = $"{ExternalPath}/Mods";
    public static bool IsAndroid => OperatingSystem.IsAndroid();
    //public static bool IsAndroid => VersionsManager.Platform == Platform.Android;

    internal static ModEntity SurvivalCraftModEntity;
    internal static ModEntity FastDebugModEntity;
    internal static bool ConfigLoaded;

    public class ModSettings {
        public string languageType = string.Empty;
    }

    public class ModHook(string name) {
        public string HookName = name;
        public List<ModLoader> Loaders = [];

        public void Add(ModLoader modLoader) {
            Loaders.Add(modLoader);
        }

        public void Remove(ModLoader modLoader) {
            Loaders.Remove(modLoader);
        }
    }

    static bool AllowContinue = true;
    public static Dictionary<string, string> Configs = [];
    /// <summary>
    /// 所有模组，含禁用、未生效的
    /// </summary>
    public static List<ModEntity> ModListAll = [];
    /// <summary>
    /// 所有已启用且生效的模组
    /// </summary>
    public static List<ModEntity> ModList = [];
    /// <summary>
    /// 含所有已启用的模组
    /// </summary>
    public static Dictionary<string, ModEntity> PackageNameToModEntity = [];
    public static List<ModLoader> ModLoaders = [];

    //仅手动禁用的
    public static Dictionary<string, HashSet<string>> DisabledMods = [];

    public static Dictionary<string, ModHook> ModHooks = [];
    public static Dictionary<string, Assembly> Dlls = [];

    public static bool GetModEntity(string packagename, out ModEntity modEntity) {
        modEntity = ModList.Find(px => px.modInfo.PackageName == packagename);
        return modEntity != null;
    }

    public static bool GetAllowContinue() => AllowContinue;

    internal static void Reboot() {
        SettingsManager.SaveSettings();
        SettingsManager.LoadSettings();
        foreach (ModEntity mod in ModList) {
            mod.Dispose();
        }
        ScreensManager.SwitchScreen("Loading");
    }

    /// <summary>
    ///     执行Hook
    /// </summary>
    public static void HookAction(string HookName, Func<ModLoader, bool> action) {
        if (ModHooks.TryGetValue(HookName, out ModHook modHook)) {
            foreach (ModLoader modLoader in modHook.Loaders) {
                if (TryInvoke(modHook, modLoader, action)) {
                    break;
                }
            }
        }
    }

    /// <summary>
    ///     反向执行Hook
    /// </summary>
    public static void HookActionReverse(string HookName, Func<ModLoader, bool> action) {
        if (ModHooks.TryGetValue(HookName, out ModHook modHook)) {
            foreach (ModLoader modLoader in Enumerable.Reverse(modHook.Loaders)) {
                if (TryInvoke(modHook, modLoader, action)) {
                    break;
                }
            }
        }
    }

    public static Dictionary<KeyValuePair<ModHook, ModLoader>, bool> m_hookBugLogged = [];

    public static bool TryInvoke(ModHook modHook, ModLoader modLoader, Func<ModLoader, bool> action) {
        try {
            if (action.Invoke(modLoader)) {
                return true;
            }
            return false;
        }
        catch (Exception ex) {
            KeyValuePair<ModHook, ModLoader> keyValuePair = new(modHook, modLoader);
            if (!m_hookBugLogged.GetValueOrDefault(keyValuePair, false)) {
                Log.Error(ex);
            }
            m_hookBugLogged[keyValuePair] = true;
            return false;
        }
    }

    public static Dictionary<string, PriorityQueue<ModLoader, long>> m_tempModHooks = [];
    static ushort m_registerHookTimes = 0;

    /// <summary>
    ///     注册Hook，优先级默认为 0<br/>
    ///     优先级相同时，执行顺序将不确定
    /// </summary>
    public static void RegisterHook(string hookName, ModLoader modLoader) {
        if (!m_tempModHooks.TryGetValue(hookName, out PriorityQueue<ModLoader, long> pq)) {
            pq = new PriorityQueue<ModLoader, long>();
            m_tempModHooks.Add(hookName, pq);
        }
        pq.Enqueue(modLoader, m_registerHookTimes++);
    }

    /// <summary>
    ///     注册Hook<br/>
    ///     优先级相同时，执行顺序将不确定
    /// </summary>
    /// <param name="hookName"></param>
    /// <param name="modLoader"></param>
    /// <param name="priority">优先级，越小越优先</param>
    public static void RegisterHook(string hookName, ModLoader modLoader, int priority) {
        if (!m_tempModHooks.TryGetValue(hookName, out PriorityQueue<ModLoader, long> pq)) {
            pq = new PriorityQueue<ModLoader, long>();
            m_tempModHooks.Add(hookName, pq);
        }
        pq.Enqueue(modLoader, ((long)priority << 16) | m_registerHookTimes++);
    }

    public static void DealWithTempModHooks() {
        foreach ((string hookName, PriorityQueue<ModLoader, long> pq) in m_tempModHooks) {
            ModHook modHook = new(hookName);
            ModHooks.Add(hookName, modHook);
            while (pq.TryDequeue(out ModLoader modLoader, out _)) {
                modHook.Add(modLoader);
            }
        }
        m_tempModHooks.Clear();
    }

    public static T GetInPakOrStorageFile<T>(string filePath, string suffix = "txt") where T : class =>
        ContentManager.Get<T>(filePath, suffix);

    public static ModInfo DeserializeJson(string json) {
        ModInfo modInfo = new();
        JsonElement jsonElement = JsonDocument.Parse(json, JsonDocumentReader.DefaultJsonOptions).RootElement;
        if (jsonElement.TryGetProperty("Name", out JsonElement name)) {
            modInfo.Name = name.GetString();
        }
        if (jsonElement.TryGetProperty("Version", out JsonElement version)
            && version.ValueKind == JsonValueKind.String) {
            modInfo.Version = version.GetString()?.Trim();
            if (modInfo.Version != null) {
                NuGetVersion.TryParse(modInfo.Version, out modInfo.NuGetVersion);
            }
        }
        if (jsonElement.TryGetProperty("ApiVersion", out JsonElement apiVersion)
            && apiVersion.ValueKind == JsonValueKind.String) {
            string apiVersionString = apiVersion.GetString()?.Trim();
            modInfo.ApiVersion = apiVersionString;
            if (apiVersionString == "1.80") {
                apiVersionString = "1.8";
            }
            else if (apiVersionString == "1.81") {
                apiVersionString = "1.8.1";
            }
            TryParseVersionRange(apiVersionString, out modInfo.ApiVersionRange);
        }
        if (jsonElement.TryGetProperty("Description", out JsonElement description)
            && description.ValueKind == JsonValueKind.String) {
            modInfo.Description = description.GetString();
        }
        if (jsonElement.TryGetProperty("ScVersion", out JsonElement scVersion)
            && scVersion.ValueKind == JsonValueKind.String) {
            modInfo.ScVersion = scVersion.GetString();
        }
        if (jsonElement.TryGetProperty("Link", out JsonElement link)
            && link.ValueKind == JsonValueKind.String) {
            modInfo.Link = link.GetString();
        }
        if (jsonElement.TryGetProperty("Author", out JsonElement author)
            && author.ValueKind == JsonValueKind.String) {
            modInfo.Author = author.GetString();
        }
        if (jsonElement.TryGetProperty("PackageName", out JsonElement packageName)
            && packageName.ValueKind == JsonValueKind.String) {
            modInfo.PackageName = packageName.GetString();
        }
        /*if (jsonElement.TryGetProperty("Email", out JsonElement Email) && Email.ValueKind == JsonValueKind.String)
        {
            modInfo.Email = packageName.GetString();
        }*/
        if (jsonElement.TryGetProperty("Dependencies", out JsonElement dependencies)) {
            if (dependencies.ValueKind == JsonValueKind.Array) {
                modInfo.Dependencies = dependencies.EnumerateArray()
                    .Where(dependency => dependency.ValueKind == JsonValueKind.String)
                    .Select(dependency => dependency.GetString())
                    .ToList();
                foreach (string dependency in modInfo.Dependencies) {
                    int index = dependency.IndexOf(':');
                    if (index != -1) {
                        string dependencyPackageName = dependency.Substring(0, index);
                        string dependencyVersion = dependency.Substring(index + 1).Trim();
                        if (TryParseVersionRange(dependencyVersion, out VersionRange dependencyVersionRange)) {
                            modInfo.DependencyRanges.Add(dependencyPackageName, dependencyVersionRange);
                        }
                    }
                    else {
                        modInfo.DependencyRanges.Add(dependency, VersionRange.All);
                    }
                }
            }
            else if (dependencies.ValueKind == JsonValueKind.Object) {
                // 版本范围入库逻辑与旧字符串写法一致；富信息（Name/Link）仅当范围成功入库才记录，
                // 保证 DependencyDetails 的键集合恒为 DependencyRanges 的子集
                void AddDependencyRange(string packageName, string version, ModDependencyDetail detail) {
                    bool added;
                    if (string.IsNullOrEmpty(version)) {
                        modInfo.DependencyRanges.Add(packageName, VersionRange.All);
                        added = true;
                    }
                    else {
                        added = TryParseVersionRange(version, out VersionRange dependencyVersionRange);
                        if (added) {
                            modInfo.DependencyRanges.Add(packageName, dependencyVersionRange);
                        }
                    }
                    if (added
                        && detail != null
                        && (detail.Name != null || detail.Link != null)) {
                        modInfo.DependencyDetails.Add(packageName, detail);
                    }
                }
                foreach (JsonProperty dependency in dependencies.EnumerateObject()) {
                    if (dependency.Value.ValueKind == JsonValueKind.String) {
                        AddDependencyRange(dependency.Name, dependency.Value.GetString()?.Trim(), null);
                    }
                    else if (dependency.Value.ValueKind == JsonValueKind.Object) {
                        // 富写法：值为对象 { Name, Version, Link }，Version 即版本范围，缺省为任意版本
                        string detailName = null;
                        string detailLink = null;
                        string dependencyVersion = null;
                        foreach (JsonProperty property in dependency.Value.EnumerateObject()) {
                            if (property.Value.ValueKind != JsonValueKind.String) {
                                continue;
                            }
                            switch (property.Name) {
                                case "Name": detailName = property.Value.GetString()?.Trim(); break;
                                case "Version": dependencyVersion = property.Value.GetString()?.Trim(); break;
                                case "Link": detailLink = property.Value.GetString()?.Trim(); break;
                            }
                        }
                        AddDependencyRange(dependency.Name, dependencyVersion, new ModDependencyDetail { Name = detailName, Link = detailLink });
                    }
                }
            }
        }
        if (jsonElement.TryGetProperty("LoadOrder", out JsonElement loadOrder)
            && loadOrder.ValueKind == JsonValueKind.Number) {
            modInfo.LoadOrder = loadOrder.GetInt32();
            //Log.Information("获取模组的Order：" + modInfo.LoadOrder);
        }
        if (jsonElement.TryGetProperty("NonPersistentMod", out JsonElement nonPersistentMod)
            && nonPersistentMod.ValueKind == JsonValueKind.True) {
            modInfo.NonPersistentMod = true;
        }
        if (jsonElement.TryGetProperty("GameplayImpactLevel", out JsonElement gameplayImpactLevel)
            && gameplayImpactLevel.ValueKind == JsonValueKind.String
            && Enum.TryParse(gameplayImpactLevel.GetString(), out GameplayImpactLevel impactLevel)) {
            modInfo.GameplayImpactLevel = impactLevel;
        }
        if (jsonElement.TryGetProperty("Settings", out JsonElement settingsArray)
            && settingsArray.ValueKind == JsonValueKind.Array) {
            // 延后解析：ParseSettings 的 ResolveType/ResolveWidget 依赖模组类型，模组 dll 此时尚未加载，
            // 由 ParseAllModSettings 在 dll 加载完成后统一解析填充 Settings。
            modInfo.RawSettings = settingsArray.Clone();
        }
        return modInfo;
    }

    /// <summary>
    /// 解析 modsettings.json：仅当根节点为数组时返回 Clone（脱离 JsonDocument 生命周期），否则返回 default。
    /// 设置项较多时可将 Settings 抽到独立的 modsettings.json，避免 modinfo.json 膨胀；其优先级高于 modinfo.json 的 Settings 字段。
    /// </summary>
    public static JsonElement TryDeserializeSettingsArray(string json) {
        JsonElement root = JsonDocument.Parse(json, JsonDocumentReader.DefaultJsonOptions).RootElement;
        return root.ValueKind == JsonValueKind.Array ? root.Clone() : default;
    }

    /// <summary>
    /// dll 加载完成后解析所有启用模组的 Settings（DeserializeJson/InitResources 阶段仅 Clone 原始 JSON 到 RawSettings，因类型解析依赖模组 dll）。
    /// RawSettings 来源优先级：modsettings.json &gt; modinfo.json 的 Settings 字段。
    /// </summary>
    public static void ParseAllModSettings() {
        foreach (ModEntity modEntity in ModList) {
            ModInfo info = modEntity.modInfo;
            if (info.RawSettings.ValueKind != JsonValueKind.Array) continue;
            try {
                info.Settings = ModSettingsParser.ParseSettings(info.RawSettings, info.PackageName);
            }
            catch (Exception e) {
                // 单模组解析失败不影响其他模组：info.Settings 保持 null，RegisterDataDriven 跳过
                if (!LanguageControl.TryGet(out string msg, ModSettingsManager.fName, "12")) msg = "Settings parse failed for mod '{0}', using empty settings: {1}";
                Log.Error("[ModSettings] " + string.Format(msg, info.PackageName, e.Message));
            }
        }
    }

    public static void SaveModSettings(XElement xElement) {
        foreach (ModEntity modEntity in ModList) {
            modEntity.SaveSettings(xElement);
        }
    }

    public static void SaveConfigs() {
        XElement element = new("Configs");
        foreach (KeyValuePair<string, string> c in Configs) {
            element.SetAttributeValue(c.Key, c.Value);
        }
        using (Stream stream = Storage.OpenFile(ConfigsPath, OpenFileMode.Create)) {
            XmlUtils.SaveXmlToStream(element, stream, Encoding.UTF8, true);
        }
    }

    public static void LoadConfigs() {
        //加载Config
        try {
            if (Storage.FileExists(ConfigsPath)) {
                using (Stream stream = Storage.OpenFile(ConfigsPath, OpenFileMode.Read)) {
                    XElement xElement = XmlUtils.LoadXmlFromStream(stream, null, true);
                    LoadConfigsFromXml(xElement);
                }
            }
        }
        catch (Exception e) {
            Log.Error($"Load configs failed. Reason: {e}");
            ConfigLoaded = false;
        }
    }

    public static void LoadConfigsFromXml(XElement xElement) {
        try {
            if (xElement.Name != "Configs") {
                return;
            }
            foreach (XAttribute c in xElement.Attributes()) {
                if (!Configs.ContainsKey(c.Name.LocalName)) {
                    SetConfig(c.Name.LocalName, c.Value);
                }
            }
            ConfigLoaded = true;
        }
        catch (Exception e) {
            Log.Error($"Load configs failed. Reason: {e}");
            ConfigLoaded = false;
        }
    }

    public static void LoadModSettings(XElement xElement) {
        foreach (ModEntity modEntity in ModList) {
            modEntity.LoadSettings(xElement);
        }
    }

    public static void SetConfig(string key, string value) {
        if (!Configs.TryAdd(key, value)) {
            Configs[key] = value;
        }
    }

    public static string ImportMod(string name, Stream stream) {
        if (!Storage.DirectoryExists(ModsPath)) {
            Storage.CreateDirectory(ModsPath);
        }
        /*if (!Storage.DirectoryExists(ProcessModListPath)) {
            Storage.CreateDirectory(ProcessModListPath);
        }*/
        string realName = name;
        if (!realName.EndsWith(ModSuffix)) {
            realName = realName + ModSuffix;
        }
        string nameWithoutSuffix = Storage.GetFileNameWithoutExtension(realName);
        string path = Storage.CombinePaths(ModsPath, realName);
        if (Storage.FileExists(path)) {
            throw new FileAlreadyExistsException(path);
        }
        using (Stream fileStream = Storage.OpenFile(path, OpenFileMode.Create)) {
            stream.CopyTo(fileStream);
        }
        return realName;
    }

    /// <summary>
    /// 对所有已生效模组进行操作（名称中的 All 是历史残留）
    /// </summary>
    public static void ModListAllDo(Action<ModEntity> entity) {
        for (int i = 0; i < ModList.Count; i++) {
            entity?.Invoke(ModList[i]);
        }
    }

    public static void Initialize() {
        if (!Storage.DirectoryExists(ModsPath)) {
            Storage.CreateDirectory(ModsPath);
        }
        ShaderCache.CacheDirectory = Storage.CombinePaths(DocPath, "ShaderCaches");
        ModHooks.Clear();
        ModListAll.Clear();
        ModList.Clear();
        PackageNameToModEntity.Clear();
        ModLoaders.Clear();
        SurvivalCraftModEntity = new SurvivalCraftModEntity();
        ModListAll.Add(SurvivalCraftModEntity);
#if !BROWSER
        FastDebugModEntity = new FastDebugModEntity();
        ModListAll.Add(FastDebugModEntity);
        GetScmods(ModsPath);
        if (!string.IsNullOrEmpty(SettingsManager.ModLoadAfters)) {
            string[] array = SettingsManager.ModLoadAfters.Split(';');
            if (array.Length > 0
                && array.Length % 2 == 0) {
                for (int i = 0; i < array.Length; i += 2) {
                    string packageName1 = array[i];
                    ModEntity modEntity1 = ModListAll.Find(x => {
                            // 排除待重启实体：LoadOrder 排序只对本进程实际加载的模组有意义
                            if (x.modInfo == null || x.IsDisabled || x.IsPendingRestart) {
                                return false;
                            }
                            return x.modInfo.PackageName == packageName1;
                        }
                    );
                    if (modEntity1 == null) {
                        continue;
                    }
                    string packageName2 = array[i + 1];
                    ModEntity modEntity2 = ModListAll.Find(x => {
                        if (x.modInfo == null || x.IsDisabled || x.IsPendingRestart) {
                            return false;
                        }
                        return x.modInfo.PackageName == packageName2;
                    });
                    if (modEntity2 == null) {
                        continue;
                    }
                    modEntity1.LoadAfter = packageName2;
                }
            }
        }
        SortModListAll();
        AppDomain.CurrentDomain.AssemblyResolve += (_, args) => {
            try {
#nullable enable
                Assembly? assembly = Dlls.GetValueOrDefault(args.Name)
                    ?? TypeCache.LoadedAssemblies.FirstOrDefault(asm => asm.GetName().FullName == args.Name);
                return assembly;
#nullable disable
            }
            catch (Exception e) {
                Log.Error($"Load assembly [{args.Name}] failed:{e}");
                Log.Debug(e);
                throw;
            }
        };
#endif
    }

    // By Gemini
    public static void SortModListAll() {
        // 1. 基础排序：先单纯按照 LoadOrder 从小到大排
        List<ModEntity> orderedMods = ModListAll.Where(m => m.modInfo != null).OrderBy(m => m.modInfo.LoadOrder).ToList();
        // 2. 准备图结构：入度表 (InDegree) 和 邻接表 (AdjList)
        //    键必须用 ModEntity 引用而非 PackageName，否则包名重复的模组会共享同一个键，导致拓扑排序错乱。
        //    ModEntity 重写了 Equals/GetHashCode（基于包名+版本），因此必须使用 ReferenceEqualityComparer 强制按引用比较。
        Dictionary<ModEntity, int> inDegree = new(ReferenceEqualityComparer.Instance);
        Dictionary<ModEntity, List<ModEntity>> adjList = new(ReferenceEqualityComparer.Instance);
        foreach (ModEntity mod in orderedMods) {
            inDegree[mod] = 0;
            adjList[mod] = [];
        }
        // 3. 构建依赖图
        foreach (ModEntity mod in orderedMods) {
            // 使用 HashSet 去重：防止 LoadAfter 和 Dependencies 里出现重复的包名
            HashSet<string> prerequisites = new HashSet<string>();
            if (mod.modInfo.DependencyRanges != null) {
                foreach (string dep in mod.modInfo.DependencyRanges.Keys) {
                    prerequisites.Add(dep);
                }
            }
            if (!string.IsNullOrEmpty(mod.LoadAfter)) {
                prerequisites.Add(mod.LoadAfter);
            }
            foreach (string pre in prerequisites) {
                // 核心需求：无视未找到的依赖项
                // 只有当这个前置模组确实存在于当前列表中时，才建立连接
                foreach (ModEntity preMod in orderedMods) {
                    if (ReferenceEquals(preMod, mod)) {
                        continue;
                    }
                    if (preMod.modInfo.PackageName == pre) {
                        adjList[preMod].Add(mod); // preMod 必须在 mod 之前加载
                        inDegree[mod]++;          // mod 的前置条件 +1
                    }
                }
            }
        }
        // 4. 稳定拓扑排序 (Kahn算法变形)
        List<ModEntity> sortedResult = [];
        List<ModEntity> remainingMods = new(orderedMods);
        while (remainingMods.Count > 0) {
            // 寻找第一个入度为 0（即所有前置模组都已加载）的模组
            // 使用 FindIndex 保证了当多个模组入度为 0 时，优先保持最初的 LoadOrder 顺序
            int index = remainingMods.FindIndex(m => inDegree[m] == 0);
            if (index != -1) {
                ModEntity currentMod = remainingMods[index];
                remainingMods.RemoveAt(index);
                sortedResult.Add(currentMod);
                // currentMod 已经“加载”，解除它对后续模组的阻塞
                foreach (ModEntity dependent in adjList[currentMod]) {
                    inDegree[dependent]--;
                }
            }
            else {
                // 触发此分支说明发生了“循环依赖”
                // 例如：用户手动设置 A 加载在 B 之后，但 B 的 Dependencies 里有 A
                // 为防止卡死或丢失模组，强行把剩下的模组按原 LoadOrder 追加到列表末尾
                sortedResult.AddRange(remainingMods);
                break;
            }
        }
        // 5. 应用排序结果
        ModListAll.Clear();
        ModListAll.AddRange(sortedResult);
    }

    public static void DisposeNotEnabledModsResources() {
        // 用引用比较：判断“该实例是否启用”与去重都应按引用，而非 ModEntity 重写的值相等
        HashSet<ModEntity> notEnabled = new(ReferenceEqualityComparer.Instance);
        foreach (ModEntity entity in ModListAll) {
            if (entity.IsDisabled || !ModList.Contains(entity, ReferenceEqualityComparer.Instance)) {
                notEnabled.Add(entity);
            }
        }
        foreach (ModEntity entity in notEnabled) {
            entity.ModArchive?.ZipFileStream.Dispose();
            entity.ModFiles.Clear();
        }
    }

    private static Task s_rescanTask;

    /// <summary>
    ///     rescan 合入完成后主线程触发；UI 订阅刷新。拉取式消费者不需要订阅（读 ModListAll 即最新）。
    /// </summary>
    public static event Action ModListRescanned;

    /// <summary>
    ///     路径归一化：统一分隔符与首尾空白，作为 ModListAll 实体与磁盘枚举两侧行对账的对齐键。
    /// </summary>
    public static string NormalizeStoragePath(string path) {
        return string.IsNullOrWhiteSpace(path) ? string.Empty : path.Replace('\\', '/').Trim();
    }

    /// <summary>
    ///     全量对账 Mods 目录与 ModListAll：新文件构造为待重启实体；实体文件消失标
    ///     IsFileDeleted；路径相同但 Size/LastWriteTime 变化（覆盖升级）追加待重启实体。
    ///     后台线程做 IO 与解析（不碰 GL，icon 只解数据），已加载快照读封送主线程，
    ///     合入（Add/排序/释放未启用流/事件）全部主线程。进行中重复调用复用现有任务。
    /// </summary>
    public static Task RescanModsAsync() {
        if (s_rescanTask != null) {
            return s_rescanTask;
        }
        s_rescanTask = Task.Run(delegate {
            try {
                RunRescan();
            }
            catch (Exception e) {
                // rescan 是后台兜底对账，失败只记日志：吞掉异常避免未观察任务异常，s_rescanTask 照常复位允许下轮重试
                Log.Error($"Rescan mods failed: {e}");
            }
            finally {
                s_rescanTask = null;
            }
        });
        return s_rescanTask;
    }

    private static void RunRescan() {
        if (!Storage.DirectoryExists(ModsPath)) {
            return;
        }

        // 磁盘侧：normalized 路径为键；diskPaths 存原路径（构造实体用），diskMeta 存元数据（对账用）
        Dictionary<string, string> diskPaths = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, (long Size, DateTime LastWrite)> diskMeta = new(StringComparer.OrdinalIgnoreCase);
        foreach (string path in EnumerateScmodFilesRecursive(ModsPath)) {
            string normalized = NormalizeStoragePath(path);
            if (normalized.Length == 0 || diskPaths.ContainsKey(normalized)) {
                continue;
            }
            try {
                diskPaths[normalized] = path;
                diskMeta[normalized] = (Storage.GetFileSize(path), Storage.GetFileLastWriteTime(path));
            }
            catch {
                // 读取元数据失败的文件本轮跳过，下轮对账再试
                diskPaths.Remove(normalized);
                diskMeta.Remove(normalized);
            }
        }

        // 已加载快照：封送主线程读（ModListAll 排序时 Clear+AddRange 不可并发枚举）
        List<ModEntity> snapshot = null;
        Dispatcher.Dispatch(() => snapshot = ModListAll.ToList(), waitUntilCompleted: true);

        List<ModEntity> fileDeleted = [];
        List<ModEntity> stalePending = [];
        HashSet<string> knownPaths = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> pendingPaths = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> replacedPaths = new(StringComparer.OrdinalIgnoreCase);
        foreach (ModEntity entity in snapshot) {
            if (entity?.ModFilePath == null) {
                continue; // 内置实体（SurvivalCraft/FastDebug）无磁盘路径
            }
            string normalized = NormalizeStoragePath(entity.ModFilePath);
            if (normalized.Length == 0) {
                continue;
            }
            if (entity.IsPendingRestart) {
                if (!diskPaths.ContainsKey(normalized)) {
                    // 待重启实体的磁盘文件被手动删除：直接从列表移除（无运行中内容，不标 IsFileDeleted）
                    stalePending.Add(entity);
                }
                else {
                    // 该路径已有待重启实体占位：构造循环不得重造，否则每轮 rescan 重复
                    // 累积副本，且新副本查重时会与旧副本互标 Duplicated
                    pendingPaths.Add(normalized);
                }
                continue;
            }
            knownPaths.Add(normalized);
            if (entity.IsFileDeleted) {
                continue; // 已是尸体：幂等，等待重启收敛
            }
            if (!diskMeta.TryGetValue(normalized, out (long Size, DateTime LastWrite) meta)) {
                fileDeleted.Add(entity);
            }
            else if (meta.Size != entity.Size || meta.LastWrite != entity.LastWriteTimeUtc) {
                // 覆盖升级：旧实体保持已加载，路径追加待重启实体。
                // 旧实体元数据不回写，此判定每轮都命中，靠 pendingPaths 占位防重复追加。
                replacedPaths.Add(normalized);
            }
        }

        // 后台构造新实体：全新路径 + 被覆盖路径
        List<ModEntity> newEntities = [];
        foreach (KeyValuePair<string, string> entry in diskPaths) {
            if (pendingPaths.Contains(entry.Key)) {
                continue; // 该路径已有待重启实体，矩阵"一致→无操作"
            }
            if (knownPaths.Contains(entry.Key) && !replacedPaths.Contains(entry.Key)) {
                continue;
            }
            if (TryCreateModEntity(entry.Value, constructAsPending: true, out ModEntity modEntity)) {
                newEntities.Add(modEntity);
            }
        }

        // 合入主线程：Add/排序/释放未启用流/事件全部主线程
        Dispatcher.Dispatch(delegate {
            foreach (ModEntity entity in stalePending) {
                // ModEntity 重写了 Equals（包名+版本），必须按引用移除，避免误删同包名同版本的其他实体
                for (int i = ModListAll.Count - 1; i >= 0; i--) {
                    if (ReferenceEquals(ModListAll[i], entity)) {
                        ModListAll.RemoveAt(i);
                    }
                }
            }
            foreach (ModEntity entity in fileDeleted) {
                entity.IsFileDeleted = true;
            }
            ModListAll.AddRange(newEntities);
            SortModListAll();
            // rescan 构造的待重启实体不进 ModList，此处即时释放其包流（与启动尾段同一释放路径）
            DisposeNotEnabledModsResources();
            ModListRescanned?.Invoke();
        }, waitUntilCompleted: true);
    }

    // 递归枚举 Mods 目录下全部 .scmod（对齐 GetScmods 的递归语义），供 rescan 磁盘侧对账
    private static IEnumerable<string> EnumerateScmodFilesRecursive(string path) {
        foreach (string item in Storage.ListFileNames(path)) {
            if (Storage.GetExtension(item).ToLowerInvariant() == ModSuffix) {
                yield return Storage.CombinePaths(path, item);
            }
        }
        foreach (string dir in Storage.ListDirectoryNames(path)) {
            foreach (string child in EnumerateScmodFilesRecursive(Storage.CombinePaths(path, dir))) {
                yield return child;
            }
        }
    }

    public static void AddException(Exception e, bool AllowContinue_ = false) {
        LoadingScreen.Error(e.ToString());
        AllowContinue = !SettingsManager.DisplayLog || AllowContinue_;
    }

    /// <summary>
    ///     获取所有文件
    /// </summary>
    /// <param name="path">文件路径</param>
    public static void GetScmods(string path) {
        foreach (string item in Storage.ListFileNames(path)) {
            if (Storage.GetExtension(item).ToLowerInvariant() == ModSuffix) {
                string filePath = Storage.CombinePaths(path, item);
                if (TryCreateModEntity(filePath, constructAsPending: false, out ModEntity modEntity)) {
                    // InitResources 只在后台解出 icon 数据（两段加载），此处主线程补 GPU 上传（Texture2D.Load 直走 GL）
                    modEntity.EnsureIconTexture();
                    ModListAll.Add(modEntity);
                }
            }
        }
        foreach (string dir in Storage.ListDirectoryNames(path)) {
            GetScmods(Storage.CombinePaths(path, dir));
        }
    }

    // 启动（GetScmods）与 rescan（RescanModsAsync）共用的实体构造路径：解密流、modinfo 校验、警告分支保持一致。
    // GetDecipherStream 返回独立内存流并自行关闭传入流，磁盘文件流 using 关闭不影响 ModArchive 存活。
    private static bool TryCreateModEntity(string filePath, bool constructAsPending, out ModEntity modEntity) {
        modEntity = null;
        try {
            using Stream stream = Storage.OpenFile(filePath, OpenFileMode.Read);
            Stream keepOpenStream = GetDecipherStream(stream);
            modEntity = new ModEntity(filePath, ZipArchive.Open(keepOpenStream, true), constructAsPending);
            // rescan 构造即待重启：磁盘有文件而本进程未加载，重启后生效（启动期构造恒为 false）。
            // 已被判定禁用的实体（破损包 modInfo==null/包名非法/重复等）不置待重启——
            // 否则管理屏"待重启"分支先于禁用分支，永不生效的坏包会显示成绿色待重启误导用户。
            modEntity.IsPendingRestart = constructAsPending && !modEntity.IsDisabled;
            if (modEntity.modInfo == null) {
                LoadingScreen.Warning(
                    $"The modinfo.json is missing or broken from [{Storage.GetFileName(modEntity.ModFilePath)}], and this mod will be disabled."
                );
            }
            else if (modEntity.IsDisabled) {
                if (modEntity.DisableReason == ModDisableReason.InvalidPackageName) {
                    LoadingScreen.Warning(
                        $"The package name [{modEntity.modInfo.PackageName}] of [{Storage.GetFileName(modEntity.ModFilePath)}] is not allowed, and this mod will not be loaded."
                    );
                    modEntity = null;
                    return false;
                }
                if (modEntity.DisableReason == ModDisableReason.Duplicated) {
                    // 包名重复通常由用户误操作导致，需要强引导；但仍加入 ModListAll 以便在管理界面查看/删除
                    AddException(new Exception($"Multiple mods with PackageName [{modEntity.modInfo.PackageName}], please keep only one."));
                }
            }
            return true;
        }
        catch (Exception e) {
            AddException(new Exception($"Failed to load mod [{Storage.GetFileName(filePath)}]: {e}"));
            modEntity = null;
            return false;
        }
    }

    public static string StreamToString(Stream stream) {
        stream.Seek(0, SeekOrigin.Begin);
        return new StreamReader(stream).ReadToEnd();
    }

    /// <summary>
    ///     将 Stream 转成 byte[]
    /// </summary>
    public static byte[] StreamToBytes(Stream stream) {
        byte[] bytes = new byte[stream.Length];
        stream.Seek(0, SeekOrigin.Begin);
        stream.ReadExactly(bytes);
        // 设置当前流的位置为流的开始
        return bytes;
    }

    [Obsolete("Use GetSha256 instead.")]
    public static string GetMd5(string input) {
#if BROWSER
        throw new NotSupportedException("MD5 is not supported on browser. Use GetSha256 instead.");
#else
        byte[] data = MD5.HashData(Encoding.Default.GetBytes(input));
        StringBuilder sBuilder = new();
        for (int i = 0; i < data.Length; i++) {
            sBuilder.Append(data[i].ToString("x2"));
        }
        return sBuilder.ToString();
#endif
    }

    public static string GetSha256(string input) {
        byte[] data = SHA256.HashData(Encoding.Default.GetBytes(input));
        StringBuilder sBuilder = new();
        for (int i = 0; i < data.Length; i++) {
            sBuilder.Append(data[i].ToString("x2"));
        }
        return sBuilder.ToString();
    }

    public static bool FindElement(XElement xElement, Func<XElement, bool> func, out XElement result) {
        result = xElement.Descendants().FirstOrDefault(func);
        return result != null;
    }

    public static bool FindElementByGuid(XElement xElement, string guid, out XElement result) {
        result = xElement.Descendants().FirstOrDefault(e => e.Attribute("Guid")?.Value == guid);
        return result != null;
    }

    public static bool HasAttribute(XElement element, Func<string, bool> func, out XAttribute xAttributeout) {
        xAttributeout = element.Attributes()
            .FirstOrDefault(a => func(a.Name.LocalName));
        return xAttributeout != null;
    }

    public static bool FindSameCraftingRecipeXElement(XElement source,
        XElement target,
        string ignoreAttribute,
        out XElement result) {
        string[] array1 = target.Value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (int i = 0; i < array1.Length; i++) {
            string str = array1[i];
            int left = str.IndexOf('"');
            array1[i] = str.Substring(left + 1, str.LastIndexOf('"') - left - 1);
        }
        return FindElement(
            source,
            ele => {
                if (ele.HasElements) {
                    return false;
                }
                foreach (XAttribute xAttribute in target.Attributes()) {
                    if (xAttribute.Name == ignoreAttribute) {
                        continue;
                    }
                    XAttribute xAttribute1 = ele.Attribute(xAttribute.Name);
                    if (xAttribute1 == null) {
                        return false;
                    }
                    if (xAttribute1.Value != xAttribute.Value) {
                        return false;
                    }
                }
                string[] array2 = ele.Value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                for (int i = 0; i < array2.Length; i++) {
                    string str = array2[i];
                    int left = str.IndexOf('"');
                    array2[i] = str.Substring(left + 1, str.LastIndexOf('"') - left - 1);
                }
                return array1.SequenceEqual(array2);
            },
            out result
        );
    }

    public static void CombineClo(XElement clothesRoot, Stream toCombineStream) {
        XElement toCombineRoot = XmlUtils.LoadXmlFromStream(toCombineStream, Encoding.UTF8, true);
        foreach (XElement element in toCombineRoot.Elements()) {
            string indexValue = element.Attribute("Index")?.Value;
            if (indexValue == null) {
                clothesRoot.Add(element);
                continue;
            }
            List<XAttribute> newAttributes = [];
            foreach (XAttribute attribute in element.Attributes()) {
                string localName = attribute.Name.LocalName;
                if (localName.StartsWith("new-") || localName.StartsWith("New-")) {
                    newAttributes.Add(attribute);
                }
            }
            if (newAttributes.Count > 0
                && FindElement(clothesRoot, e => e.Attribute("Index")?.Value == indexValue, out XElement element1)) {
                foreach (XAttribute newAttribute in newAttributes) {
                    element1.SetAttributeValue(newAttribute.Name.LocalName.Substring(4), newAttribute.Value);
                }
            }
            else if (HasAttribute(element, name => name.StartsWith("r-") || name == "Remove", out XAttribute _)
                && FindElement(clothesRoot, e => e.Attribute("Index")?.Value == indexValue, out XElement element2)) {
                element2.Remove();
            }
            else {
                clothesRoot.Add(element);
            }
        }
    }

    public static void CombineCr(XElement crRoot, Stream toCombineStream) {
        XElement toCombineRoot = XmlUtils.LoadXmlFromStream(toCombineStream, Encoding.UTF8, true);
        CombineCrLogic(crRoot, toCombineRoot);
    }

    public static void CombineCrLogic(XElement crRoot, XElement toCombineRoot) {
        foreach (XElement element in toCombineRoot.Elements()) {
            if (element.Attribute("Result") != null) {
                if (HasAttribute(element, name => name.StartsWith("new-") || name.StartsWith("New-"), out XAttribute attribute)) {
                    if (FindSameCraftingRecipeXElement(crRoot, element, attribute.Name.LocalName, out XElement element1)) {
                        element1.SetAttributeValue(attribute.Name.LocalName.Substring(4), attribute.Value);
                        element1.SetValue(element.Value);
                    }
                }
                else if (HasAttribute(element, name => name.StartsWith("r-") || name == "Remove", out XAttribute attribute1)) {
                    if (FindSameCraftingRecipeXElement(crRoot, element, attribute1.Name.LocalName, out XElement element1)) {
                        element1.Remove();
                    }
                }
                else {
                    crRoot.Add(element);
                }
            }
            else {
                CombineCrLogic(crRoot, element);
            }
        }
    }

    /// <summary>
    /// 仅用于合并 Database
    /// </summary>
    public static void Modify(XElement source, XElement change) {
        if (FindElement(
                source,
                item => item.Name.LocalName == change.Name.LocalName
                    && item.Attribute("Guid") != null
                    && change.Attribute("Guid") != null
                    && item.Attribute("Guid")?.Value == change.Attribute("Guid")?.Value,
                out XElement xElement1
            )) {
            foreach (XElement xElement in change.Elements()) {
                Modify(xElement1, xElement);
            }
        }
        else {
            source.Add(change);
        }
    }

    public class ClassSubstitute: IEquatable<ClassSubstitute> {
        public string PackageName;
        public string ClassName;

        public ClassSubstitute(string packageName, string className) {
            PackageName = packageName;
            ClassName = className;
        }

        public bool Equals(ClassSubstitute other) {
            if (other is null) {
                return false;
            }
            if (ReferenceEquals(this, other)) {
                return true;
            }
            return PackageName == other.PackageName && ClassName == other.ClassName;
        }

        public override bool Equals(object obj) {
            return Equals(obj as ClassSubstitute);
        }

        public override int GetHashCode() => HashCode.Combine(PackageName, ClassName);

        public static bool operator ==(ClassSubstitute left, ClassSubstitute right) => Equals(left, right);

        public static bool operator !=(ClassSubstitute left, ClassSubstitute right) => !Equals(left, right);
    }

    public static FrozenDictionary<string, string> ImportantDatabaseClasses;
    public static Dictionary<string, List<ClassSubstitute>> ClassSubstitutes = [];
    public static Dictionary<string, List<ClassSubstitute>> OldClassSubstitutes = [];
    public static Dictionary<string, ClassSubstitute> SelectedClassSubstitutes = [];

    //对于关键（绑定了API1.7新的ModLoader接口的）组件，对修改行为进行检查报错
    //修饰就是用的internal，不提供其他模组的调用权限
    internal static void InitImportantDatabaseClasses() {
        if (ModList.Count <= 3) {
            return;
        }
        ImportantDatabaseClasses = new KeyValuePair<string, string>[] {
            new("7347a83f-2d46-4fdf-bce2-52677de0b568", "Game.ComponentBody"),
            new("4e14ce27-fdef-46ca-8ea0-26af43c215e5", "Game.ComponentHealth"),
            new("7ecfafc4-4603-424c-87dd-1df59e7ef413", "Game.ComponentPlayer"),
            new("9dc356e5-7dc8-45f6-8779-827ddee9966c", "Game.ComponentMiner"),
            new("6f538db3-f1fe-4e91-8ef5-627c0b1a74ba", "Game.ComponentRunAwayBehavior"),
            new("8b3d07dc-6498-4691-9686-cf4edabb8f3f", "Game.ComponentGui"),
            new("e2636c38-f179-4aa1-b087-ed6920d66e8e", "Game.SubsystemTerrain"),
            new("96e79f99-a082-4190-9ab6-835dc49ebbdd", "Game.SubsystemExplosions"),
            new("dafb8e14-11b9-44b7-a208-424b770aeaa9", "Game.SubsystemProjectiles"),
            new("32d392de-69c1-4d04-9e0b-5c7463201892", "Game.SubsystemPickables"),
            new("54a4f6d5-98dd-4dc3-bf6d-04dfd972c6b7", "Game.SubsystemTime"),
            new("b2e68ecd-49fc-4c05-b784-424da13f8550", "Game.ComponentDispenser"),
            new("f6b020bb-8994-6ae6-289b-a842e3eb9ca5", "Game.ComponentFactors"),
            new("a346c456-5087-48c4-835a-5829b3f35c68", "Game.ComponentLevel"),
            new("1df4e627-c959-4e6a-bfa2-b7ee3ef08c99", "Game.ComponentClothing"),
        }.ToFrozenDictionary();
    }

    public static void CombineDataBase(XElement databaseRoot, Stream toCombineStream) {
        CombineDataBase(databaseRoot, toCombineStream, string.Empty);
    }

    public static void CombineDataBase(XElement databaseRoot, Stream toCombineStream, string modPackageName) {
        XElement toCombineRoot = XmlUtils.LoadXmlFromStream(toCombineStream, Encoding.UTF8, true);
        XElement databaseObjects = databaseRoot.Element("DatabaseObjects");

        // 为新增的 EntityTemplate 添加 ModSource 参数
        if (!string.IsNullOrEmpty(modPackageName) && databaseObjects != null) {
            HashSet<string> existingGuids = new(
                databaseObjects.Descendants("EntityTemplate").Attributes("Guid").Select(a => a.Value),
                StringComparer.OrdinalIgnoreCase
            );
            //不能只遍历根元素，否则无法识别到嵌套在folder里的EntityTemplate
            foreach (XElement entity in toCombineRoot.Descendants("EntityTemplate")) {
                XAttribute guidAttr = entity.Attribute("Guid");
                bool isNewEntity = guidAttr == null || !existingGuids.Contains(guidAttr.Value);
                if (isNewEntity) {
                    // 如果已经有 ModSource 就不重复添加
                    bool hasModSource = entity.Elements("Parameter").Any(p => p.Attribute("Name")?.Value == "ModSource");
                    if (!hasModSource) {
                        XElement parameterElement = new("Parameter");
                        parameterElement.SetAttributeValue("Name", "ModSource");
                        parameterElement.SetAttributeValue("Value", modPackageName);
                        parameterElement.SetAttributeValue("Type", "string");
                        entity.Add(parameterElement);
                        existingGuids.Add(guidAttr.Value);//防止同一批次的同guid后续元素被误判为新元素
                    }
                }
            }
        }

        foreach (XElement element in toCombineRoot.Elements()) {
            if (element.Attribute("Remove") != null) {
                XAttribute guidAttribute = element.Attribute("Guid");
                if (guidAttribute == null) {
                    continue;
                }
                if (FindElementByGuid(databaseObjects, guidAttribute.Value, out XElement oldElement)) {
                    oldElement.Remove();
                }
            }
            //处理修改
            else if (HasAttribute(element, str => str.StartsWith("new-") || str.StartsWith("New-"), out XAttribute newAttribute)) {
                XAttribute guidAttribute = element.Attribute("Guid");
                if (guidAttribute == null) {
                    continue;
                }
                string guid = guidAttribute.Value;
                if (FindElementByGuid(databaseObjects, guid, out XElement oldElement)) {
                    string newAttributeName = newAttribute.Name.LocalName.Substring(4);
                    if (newAttributeName == "Value"
                        && oldElement.Attribute("Name")?.Value == "Class") {
                        if (ClassSubstitutes.TryGetValue(guid, out List<ClassSubstitute> classSubstitutes)) {
                            classSubstitutes.Add(new ClassSubstitute(modPackageName, newAttribute.Value));
                        }
                        else {
                            ClassSubstitutes.Add(
                                guid,
                                [new ClassSubstitute("survivalcraft", oldElement.Attribute("Value")!.Value), new ClassSubstitute(modPackageName, newAttribute.Value)]
                            );
                        }
                    }
                    else {
                        oldElement.SetAttributeValue(newAttributeName, newAttribute.Value);
                    }
                }
            }
            else {
                Modify(databaseObjects, element);
            }
        }
    }
    public static void DealWithClassSubstitutes() {
        if (ClassSubstitutes.Count > 0) {
            Queue<(string, XElement)> needToSolves = [];
            foreach ((string guid, List<ClassSubstitute> substitutes) in ClassSubstitutes) {
                // 如果有 2 个或更多候选项
                if (substitutes.Count >= 2 && FindElementByGuid(DatabaseManager.DatabaseNode, guid, out XElement element)) {
                    // 如果手动选择过
                    if (SelectedClassSubstitutes.TryGetValue(guid, out ClassSubstitute selected)) {
                        // 如果选择项还能从候选项找到
                        if (substitutes.Any(x => x == selected)) {
                            if (OldClassSubstitutes.TryGetValue(guid, out List<ClassSubstitute> oldSubstitutes)) {
                                // 如果候选项与旧候选项一致，则使用选择项
                                if (substitutes.Count == oldSubstitutes.Count && oldSubstitutes.SequenceEqual(substitutes)) {
                                    element.SetAttributeValue("Value", selected.ClassName);
                                }
                                // 否则需要手动重选
                                else {
                                    SelectedClassSubstitutes.Remove(guid);
                                    needToSolves.Enqueue((guid, element));
                                }
                            }
                            else {
                                element.SetAttributeValue("Value", selected.ClassName);
                            }
                        }
                        else {
                            SelectedClassSubstitutes.Remove(guid);
                            needToSolves.Enqueue((guid, element));
                        }
                    }
                    // 未手动选择过
                    // 当只有两个候选项，且不重要时，直接使用第二个（第一个是原版的）
                    else if (substitutes.Count == 2 && !(ImportantDatabaseClasses?.ContainsKey(guid) ?? false)) {
                        element.SetAttributeValue("Value", substitutes.Last().ClassName);
                    }
                    else {
                        needToSolves.Enqueue((guid, element));
                    }
                }
                else {
                    SelectedClassSubstitutes.Remove(guid);
                }
            }
            if (needToSolves.Count > 0) {
                AllowContinue = false;
                void Handle() {
                    if (needToSolves.TryDequeue(out (string, XElement) tuple)) {
                        DialogsManager.ShowDialog(ScreensManager.RootWidget, new SelectClassSubstituteDialog(tuple.Item1, tuple.Item2, Handle));
                    }
                    else {
                        AllowContinue = true;
                    }
                }
                Handle();
            }
        }
        else {
            SelectedClassSubstitutes.Clear();
        }
    }

    public static bool TryParseVersionRange(string value, out VersionRange versionRange) {
        if (string.IsNullOrEmpty(value)) {
            versionRange = null;
            return false;
        }
        value = value.Trim();
        if (value.Length == 0) {
            versionRange = null;
            return false;
        }
        char firstChar = value[0];
        switch (firstChar) {
            case '=': {
                if (NuGetVersion.TryParse(value.Substring(1), out NuGetVersion nuGetVersion)) {
                    versionRange = new VersionRange(nuGetVersion, true, nuGetVersion, true);
                    return true;
                }
                break;
            }
            case '>': {
                if (value.Length > 1) {
                    if (value[1] == '=') {
                        if (NuGetVersion.TryParse(value.Substring(2), out NuGetVersion nuGetVersion)) {
                            versionRange = new VersionRange(nuGetVersion, true);
                            return true;
                        }
                    }
                    else {
                        if (NuGetVersion.TryParse(value.Substring(1), out NuGetVersion nuGetVersion)) {
                            versionRange = new VersionRange(nuGetVersion, false);
                            return true;
                        }
                    }
                }
                break;
            }
            case '<': {
                if (value.Length > 1) {
                    if (value[1] == '=') {
                        if (NuGetVersion.TryParse(value.Substring(2), out NuGetVersion nuGetVersion)) {
                            versionRange = new VersionRange(null, false, nuGetVersion, true);
                            return true;
                        }
                    }
                    else {
                        if (NuGetVersion.TryParse(value.Substring(1), out NuGetVersion nuGetVersion)) {
                            versionRange = new VersionRange(null, false, nuGetVersion);
                            return true;
                        }
                    }
                }
                break;
            }
            case '^': {
                if (NuGetVersion.TryParse(value.Substring(1), out NuGetVersion nuGetVersion)) {
                    versionRange = new VersionRange(nuGetVersion, true, new NuGetVersion(nuGetVersion.Major + 1, 0, 0, 0));
                    return true;
                }
                break;
            }
            case '~': {
                if (NuGetVersion.TryParse(value.Substring(1), out NuGetVersion nuGetVersion)) {
                    versionRange = new VersionRange(nuGetVersion, true, new NuGetVersion(nuGetVersion.Major, nuGetVersion.Minor + 1, 0, 0));
                    return true;
                }
                break;
            }
            default:
                if (VersionRange.TryParse(value, out versionRange)) {
                    return true;
                }
                break;
        }
        versionRange = null;
        return false;
    }

    public static string HeadingCode = "有头有脸天才少年,耍猴表演敢为人先";
    public static string HeadingCode2 = "修改他人mod请获得原作者授权，否则小心出名！";

    public static Stream GetDecipherStream(Stream stream) {
        MemoryStream keepOpenStream = new();
        byte[] buff = new byte[stream.Length];
        stream.ReadExactly(buff);
        byte[] hc = Encoding.UTF8.GetBytes(HeadingCode);
        bool decipher = true;
        for (int i = 0; i < hc.Length; i++) {
            if (hc[i] != buff[i]) {
                decipher = false;
                break;
            }
        }
        byte[] hc2 = Encoding.UTF8.GetBytes(HeadingCode2);
        bool decipher2 = true;
        for (int i = 0; i < hc2.Length; i++) {
            if (hc2[i] != buff[i]) {
                decipher2 = false;
                break;
            }
        }
        if (decipher) {
            byte[] buff2 = new byte[buff.Length - hc.Length];
            for (int i = 0; i < buff2.Length; i++) {
                buff2[i] = buff[buff.Length - 1 - i];
            }
            keepOpenStream.Write(buff2, 0, buff2.Length);
            keepOpenStream.Flush();
        }
        else if (decipher2) {
            byte[] buff2 = new byte[buff.Length - hc2.Length];
            int k = 0;
            int t = 0;
            int l = (buff2.Length + 1) / 2;
            for (int i = 0; i < buff2.Length; i++) {
                if (i % 2 == 0) {
                    buff2[i] = buff[hc2.Length + k];
                    k++;
                }
                else {
                    buff2[i] = buff[hc2.Length + l + t];
                    t++;
                }
            }
            keepOpenStream.Write(buff2, 0, buff2.Length);
            keepOpenStream.Flush();
        }
        else {
            stream.Position = 0L;
            stream.CopyTo(keepOpenStream);
        }
        stream.Dispose();
        keepOpenStream.Position = 0L;
        return keepOpenStream;
    }

    public static bool StrengtheningMod(string path) {
        Stream stream = Storage.OpenFile(path, OpenFileMode.Read);
        byte[] buff = new byte[stream.Length];
        stream.ReadExactly(buff);
        byte[] hc = Encoding.UTF8.GetBytes(HeadingCode);
        bool decipher = true;
        for (int i = 0; i < hc.Length; i++) {
            if (hc[i] != buff[i]) {
                decipher = false;
                break;
            }
        }
        byte[] hc2 = Encoding.UTF8.GetBytes(HeadingCode2);
        bool decipher2 = true;
        for (int i = 0; i < hc2.Length; i++) {
            if (hc2[i] != buff[i]) {
                decipher2 = false;
                break;
            }
        }
        if (decipher || decipher2) {
            return false;
        }
        byte[] buff2 = new byte[buff.Length + hc2.Length];
        int k = 0;
        int l = hc2.Length;
        for (int i = 0; i < hc2.Length; i++) {
            buff2[i] = hc2[i];
        }
        for (int i = 0; i < buff.Length; i++) {
            if (i % 2 == 0) {
                buff2[k + l] = buff[i];
                k++;
            }
        }
        k = 0;
        l = hc2.Length + (buff.Length + 1) / 2;
        for (int i = 0; i < buff.Length; i++) {
            if (i % 2 != 0) {
                buff2[k + l] = buff[i];
                k++;
            }
        }
        string newPath = $"{path.Substring(0, path.LastIndexOf('.'))}({LanguageControl.Get("ModsManageContentScreen", 63)}).scmod";
        FileStream fileStream = new(Storage.GetSystemPath(newPath), FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite);
        fileStream.Write(buff2, 0, buff2.Length);
        fileStream.Flush();
        stream.Dispose();
        fileStream.Dispose();
        return true;
    }

    public static void ShowDisabledModsDialog() {
        StringBuilder sb = new();
        foreach (ModEntity entity in ModListAll) {
            if (entity.IsDisabled
                && entity.DisableReason >= ModDisableReason.NoModInfo) {
                sb.AppendLine(Storage.GetFileName(entity.ModFilePath));
            }
        }
        if (sb.Length > 0) {
            sb.Insert(0, $"{LanguageControl.Get(fName, "7")}\n");
            DialogsManager.ShowDialog(
                null,
                new MessageDialog(
                    LanguageControl.Warning,
                    sb.ToString(),
                    LanguageControl.Yes,
                    LanguageControl.No,
                    button => {
                        if (button == MessageDialogButton.Button1) {
                            ScreensManager.SwitchScreen("ModsManageContent");
                        }
                    }
                )
            );
        }
    }
}