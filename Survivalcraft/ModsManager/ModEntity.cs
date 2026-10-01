using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using Engine;
using Engine.Graphics;
using NuGet.Versioning;

namespace Game {
    public class ModEntity {
        public ModInfo modInfo;
        public Texture2D Icon;
        public ZipArchive ModArchive;
        public Dictionary<string, ZipArchiveEntry> ModFiles = [];
        public List<Type> BlockTypes = [];
        public string ModFilePath;
        public bool IsDisabled;
        public ModDisableReason DisableReason = ModDisableReason.Unknown;
        public long Size;
        // 待重启是“未生效”而非禁用，与 IsDisabled 正交：磁盘有文件、包有效，本进程未加载，重启后生效
        public bool IsPendingRestart;
        // 磁盘文件已消失的“运行中尸体”：本体仍在 ModList（运行中可能读包内资源），重启后随 Initialize() 重建自然消失
        public bool IsFileDeleted;
        // 构造时记录的文件修改时间（Utc），供 rescan 对账识别“同路径覆盖升级”
        public DateTime LastWriteTimeUtc;
        // icon 两段加载的中间产物：后台线程解出的图像数据，GPU 上传延迟到主线程 EnsureIconTexture
        private byte[] m_iconData;
        // 构造相态：true = 以待重启实体构造（rescan 路径），查重豁免与日志分支据此区分
        private bool m_constructAsPending;
        public bool IsDependencyChecked;
        public string LoadAfter;
        public static HashSet<string> InvalidDllNames = ["Survivalcraft.dll", "Engine.dll", "EntitySystem.dll"];
        public const string fName = "ModEntity";

        /// <summary>
        /// 获取和设置模组的主要 ModLoader
        /// </summary>
        public ModLoader Loader {
            get => ModLoader_;
            set => ModLoader_ = value;
        }
        public List<ModLoader> Loaders {
            get; set;
        } = [];

        ModLoader ModLoader_;

        public ModEntity() { }

        public ModEntity(ZipArchive zipArchive) {
            ModFilePath = ModsManager.ModsPath;
            ModArchive = zipArchive;
            InitResources();
        }

        // constructAsPending：以待重启实体构造（rescan 对账路径）。启动期构造保持 false。
        public ModEntity(string FileName, ZipArchive zipArchive, bool constructAsPending = false) {
            ModFilePath = FileName;
            Size = Storage.GetFileSize(FileName);
            LastWriteTimeUtc = Storage.GetFileLastWriteTime(FileName);
            m_constructAsPending = constructAsPending;
            ModArchive = zipArchive;
            InitResources();
        }

        public virtual void LoadIcon(Stream stream) {
            Icon = Texture2D.Load(stream);
            stream.Close();
        }

        // icon 两段加载的第一段：后台线程只能解数据（纯 CPU），GPU 上传直走 GL 必须留在主线程。
        // guard 保证 webp 先到先得，保持原 "webp 优先于 png" 语义。
        private void ReadIconData(Stream stream) {
            if (m_iconData != null) {
                stream.Close();
                return;
            }
            using MemoryStream ms = new();
            stream.CopyTo(ms);
            m_iconData = ms.ToArray();
            stream.Close();
        }

        /// <summary>
        ///     主线程调用：icon 图像数据已解出而纹理未建时上传 GPU；无数据则保持 null（UI 有 null 兜底）。
        /// </summary>
        public void EnsureIconTexture() {
            if (Icon == null && m_iconData != null) {
                using MemoryStream ms = new(m_iconData, writable: false);
                Icon = Texture2D.Load(ms);
            }
        }

        /// <summary>
        ///     获取模组的文件时调用。
        /// </summary>
        /// <param name="extension">文件扩展名</param>
        /// <param name="action">参数1文件名参数，2打开的文件流</param>
        public virtual void GetFiles(string extension, Action<string, Stream> action) {
            //将每个zip里面的文件读进内存中
            bool skip = false;
            Loader?.GetModFiles(extension, action, out skip);
            if (skip || ModArchive == null) {
                return;
            }
            foreach ((string filename, ZipArchiveEntry zipArchiveEntry) in ModFiles) {
                if (Storage.GetExtension(filename) == extension) {
                    MemoryStream stream = new();
                    ModArchive.ExtractFile(zipArchiveEntry, stream);
                    stream.Position = 0L;
                    try {
                        action.Invoke(zipArchiveEntry.FilenameInZip, stream);
                    }
                    catch (Exception e) {
                        Log.Error($"Get file [{zipArchiveEntry.FilenameInZip}] failed: {e}");
                    }
                    finally {
                        stream.Dispose();
                    }
                }
            }
        }

        /// <param name="extension">文件扩展名</param>
        /// <param name="action">参数1文件名参数，2打开的文件流</param>
        /// <return>列表是否为空</return>
        public virtual bool GetFilesAndExist(string extension, Action<string, Stream> action) {
            if (ModArchive?.ReadCentralDir().Count != 0) {
                GetFiles(extension, action);
                return false;
            }
            return true;
        }

        /// <summary>
        ///     获取指定文件
        /// </summary>
        /// <param name="filename"></param>
        /// <param name="action">参数1打开的文件流</param>
        /// <returns></returns>
        public virtual bool GetFile(string filename, Action<Stream> action) {
            bool skip = false;
            bool loaderReturns = false;
            Loader?.GetModFile(filename, action, out skip, out loaderReturns);
            if (skip || ModArchive == null) {
                return loaderReturns;
            }
            if (ModFiles.TryGetValue(filename, out ZipArchiveEntry entry)) {
                using MemoryStream ms = new();
                ModArchive.ExtractFile(entry, ms);
                ms.Position = 0L;
                try {
                    action?.Invoke(ms);
                }
                catch (Exception e) {
                    LoadingScreen.Error($"[{modInfo.Name}] Get file [{filename}] failed: {e}");
                }
                return false;
            }
            return true;
        }

        public virtual bool GetAssetsFile(string filename, Action<Stream> action) => GetFile($"Assets/{filename}", action);

        /// <summary>
        ///     初始化语言包
        /// </summary>
        public virtual void LoadLauguage() {
            GetAssetsFile(
                "Lang/en-US.json",
                stream => {
                    LoadingScreen.Info($"[{modInfo.Name}] Loading English Language file");
                    LanguageControl.LoadEnglishJson(stream);
                }
            );
            string language = ModsManager.Configs["Language"];
            if (language == "en-US") {
                return;
            }
            GetAssetsFile(
                $"Lang/{language}.json",
                stream => {
                    LoadingScreen.Info($"[{modInfo.Name}] Loading Current Language file");
                    LanguageControl.loadJson(stream);
                }
            );
        }

        /// <summary>
        ///     初始化Content资源
        /// </summary>
        public virtual void InitResources() {
            ModFiles.Clear();
            if (ModArchive == null) {
                return;
            }
            List<ZipArchiveEntry> entries = ModArchive.ReadCentralDir();
            foreach (ZipArchiveEntry zipArchiveEntry in entries) {
                if (zipArchiveEntry.FileSize > 0) {
                    ModFiles.Add(zipArchiveEntry.FilenameInZip, zipArchiveEntry);
                }
            }
            if (GetFile("icon.webp", ReadIconData)) {
                GetFile("icon.png", ReadIconData);
            }
            GetFile("modinfo.json",
                stream => {
                    try {
                        modInfo = ModsManager.DeserializeJson(ModsManager.StreamToString(stream));
                    }
                    catch (Exception e) {
                        Log.Error($"Deserialize modinfo.json from [{Storage.GetFileName(ModFilePath)}] failed: {e}");
                    }
                });
            // modsettings.json 优先级高于 modinfo.json 的 Settings 字段；不存在或不合法时沿用 modinfo.json 的 RawSettings。
            // 必须在 SafeMode 释放 ModArchive 之前读取（ParseAllModSettings 阶段归档可能已释放）。
            GetFile("modsettings.json",
                stream => {
                    try {
                        JsonElement settings = ModsManager.TryDeserializeSettingsArray(ModsManager.StreamToString(stream));
                        if (settings.ValueKind == JsonValueKind.Array) {
                            modInfo.RawSettings = settings;
                        }
                    }
                    catch (Exception e) {
                        Log.Error($"Deserialize modsettings.json from [{Storage.GetFileName(ModFilePath)}] failed: {e}");
                    }
                });
            if (SettingsManager.SafeMode && this is not SurvivalCraftModEntity) {
                ModFiles.Clear();
                ModArchive?.ZipFileStream.Dispose();
                ModArchive = null;
            }
            if (modInfo == null) {
                IsDisabled = true;
                DisableReason = ModDisableReason.NoModInfo;
                return;
            }
            if (string.IsNullOrEmpty(modInfo.PackageName)
                || (ModsManager.ModListAll.Count >= 2 && (modInfo.PackageName == "survivalcraft" || modInfo.PackageName == "fastdebug"))
                || modInfo.PackageName.Contains(';')
                || modInfo.PackageName.Contains('\n')) {
                IsDisabled = true;
                DisableReason = ModDisableReason.InvalidPackageName;
                return;
            }
            // 手动禁用检查必须先于包名查重：被禁用的模组"占名不占位"（不加载、不该连累别人）。
            // 否则"禁用旧版 + 下载同包名新版"重启后，后构造方查重命中先构造方，
            // 互标 Duplicated 把手动禁用升级成重复禁用，新版也被冤杀。
            if (ModsManager.DisabledMods.TryGetValue(modInfo.PackageName, out HashSet<string> disabledVersions)
                && disabledVersions.Contains(modInfo.Version)) {
                IsDisabled = true;
                DisableReason = ModDisableReason.Manually;
                return;
            }
            // 查重跳过手动禁用的对手：同包名"禁用旧版 + 正常新版"是合法共存态。
            // 不排除 Duplicated/依赖错误等已禁用对手——三副本场景靠链式互标保持全禁。
            ModEntity temp = ModsManager.ModListAll.FirstOrDefault(x => x.modInfo?.PackageName == modInfo.PackageName
                && !(x.IsDisabled && x.DisableReason == ModDisableReason.Manually));
            // 相态不同（本实体待重启、对手已加载，或反之）即"覆盖升级待重启"状态，合法豁免；
            // 仅同相态互标 Duplicated（已加载 vs 已加载、待重启 vs 待重启，后者引导用户删一个）。
            // 启动期 ModListAll 全重建，不会遇到上轮 rescan 残留的 pending 实体，此分支只为健壮性保留。
            bool bothSamePhase = temp != null && (m_constructAsPending == temp.IsPendingRestart);
            if (bothSamePhase) {
                IsDisabled = true;
                DisableReason = ModDisableReason.Duplicated;
                temp.IsDisabled = true;
                temp.DisableReason = ModDisableReason.Duplicated;
                return;
            }
            if (m_constructAsPending) {
                // 待重启实体尚未加载内容，不打"已加载"日志避免误导
                LoadingScreen.Info($"[{modInfo.Name}](Version: {modInfo.Version}) Pending restart.");
            }
            else {
                LoadingScreen.Info($"[{modInfo.Name}](Version: {modInfo.Version}) Loaded {ModFiles.Count} resource files.");
            }
        }

        public virtual void CombineContent() {
            foreach (KeyValuePair<string, ZipArchiveEntry> c in ModFiles) {
                ZipArchiveEntry zipArchiveEntry = c.Value;
                string filename = zipArchiveEntry.FilenameInZip;
                if (!zipArchiveEntry.IsFilenameUtf8) {
                    ModsManager.AddException(
                        new Exception(
                            $"[{modInfo.Name}] The file name [{zipArchiveEntry.FilenameInZip}] is not encoded in UTF-8, need to be corrected."
                        )
                    );
                }
                if (filename.StartsWith("Assets/")) {
                    MemoryStream memoryStream = new();
                    ContentInfo contentInfo = new(filename.Substring(7));
                    ModArchive.ExtractFile(zipArchiveEntry, memoryStream);
                    contentInfo.SetContentStream(memoryStream);
                    ContentManager.Add(contentInfo);
                }
            }
        }

        /// <summary>
        ///     初始化BlocksData资源
        /// </summary>
        public virtual void LoadBlocksData() {
            bool flag = true;
            GetFiles(
                ".csv",
                (_, stream) => {
                    if (flag) {
                        LoadingScreen.Info($"[{modInfo.Name}] {LanguageControl.Get(fName, "1")}");
                        flag = false;
                    }
                    BlocksManager.LoadBlocksData(ModsManager.StreamToString(stream));
                }
            );
        }

        /// <summary>
        ///     初始化Database数据
        /// </summary>
        /// <param name="xElement"></param>
        public virtual void LoadXdb(ref XElement xElement) {
            bool flag = true;
            XElement element = xElement;
            GetFiles(
                ".xdb",
                (_, stream) => {
                    if (flag) {
                        LoadingScreen.Info($"[{modInfo.Name}] {LanguageControl.Get(fName, "2")}");
                        flag = false;
                    }
                    ModsManager.CombineDataBase(element, stream, modInfo.PackageName);
                }
            );
            foreach(ModLoader item in Loaders)
                item.OnXdbLoad(xElement);
        }

        /// <summary>
        ///     初始化Clothing数据
        /// </summary>
        /// <param name="block"></param>
        /// <param name="xElement"></param>
        // ReSharper disable UnusedParameter.Global
        public virtual void LoadClo(ClothingBlock block, ref XElement xElement)
            // ReSharper restore UnusedParameter.Global
        {
            bool flag = true;
            XElement element = xElement;
            GetFiles(
                ".clo",
                (_, stream) => {
                    if (flag) {
                        LoadingScreen.Info($"[{modInfo.Name}] {LanguageControl.Get(fName, "3")}");
                        flag = false;
                    }
                    ModsManager.CombineClo(element, stream);
                }
            );
        }

        /// <summary>
        ///     初始化CraftingRecipe
        /// </summary>
        /// <param name="xElement"></param>
        public virtual void LoadCr(ref XElement xElement) {
            bool flag = true;
            XElement element = xElement;
            GetFiles(
                ".cr",
                (_, stream) => {
                    if (flag) {
                        LoadingScreen.Info($"[{modInfo.Name}] {LanguageControl.Get(fName, "4")}");
                        flag = false;
                    }
                    ModsManager.CombineCr(element, stream);
                }
            );
        }

        /// <summary>
        ///     加载mod程序集
        /// </summary>
        public virtual Assembly[] GetAssemblies() {
            bool flag = true;
            List<Assembly> assemblies = new();
            GetFiles(
                ".dll",
                (filename, stream) => {
                    if (flag) {
                        LoadingScreen.Info($"[{modInfo.Name}] Loading .dll assembly files.");
                        flag = false;
                    }
                    if (!filename.StartsWith("Assets/")) {
                        string fileNameWithoutDirectory = Storage.GetFileName(filename);
                        if (!InvalidDllNames.Contains(fileNameWithoutDirectory)) {
#pragma warning disable IL2026
                            assemblies.Add(Assembly.Load(ModsManager.StreamToBytes(stream)));
#pragma warning restore IL2026
                        }
                    }
                }
            ); //获取mod文件内的dll文件（不包括Assets目录内的dll）
            return assemblies.ToArray();
        }

        public virtual void HandleAssembly(Assembly assembly) {
            List<Type> blockTypes = [];
#pragma warning disable IL2026
            Type[] types = assembly.GetTypes();
#pragma warning restore IL2026
            foreach (Type type in types) {
                if (type.IsSubclassOf(typeof(ModLoader))
                    && !type.IsAbstract) {
#pragma warning disable IL2062
                    if (Activator.CreateInstance(type) is ModLoader modLoader) {
#pragma warning disable IL2062
                        modLoader.Entity = this;
                        modLoader.__ModInitialize();
                        ModsManager.ModLoaders.Add(modLoader);
                        Loaders.Add(modLoader);
                    }
                }
                else if (type.IsSubclassOf(typeof(IContentReader.IContentReader))
                    && !type.IsAbstract
#pragma warning disable IL2062
                    && Activator.CreateInstance(type) is IContentReader.IContentReader reader) {
#pragma warning restore IL2062
                    ContentManager.ReaderList.TryAdd(reader.Type, reader);
                }
                else if (type.IsSubclassOf(typeof(Block))
                    && !type.IsAbstract) {
                    blockTypes.Add(type);
                }
                else if (type.IsSubclassOf(typeof(SubsystemCreatureSpawn.CreatureType))
                    && !type.IsAbstract) {
                    SubsystemCreatureSpawn.m_creatureSpawnRules.TryAdd(type.FullName, type);
                }
            }
            BlockTypes.AddRange(blockTypes);
            Loaders.Sort((x, y) => x.Priority.CompareTo(y.Priority));
            Loader = Loaders.FirstOrDefault();
        }

        public virtual void LoadJs() {
#if !IOS && !BROWSER
            bool flag = true;
            GetFiles(
                ".js",
                (_, stream) => {
                    if (flag) {
                        LoadingScreen.Info($"[{modInfo.Name}] {LanguageControl.Get(fName, "5")}");
                        flag = false;
                    }
                    JsInterface.Execute(new StreamReader(stream).ReadToEnd());
                }
            );
#endif
        }

        /// <summary>
        ///     检查依赖项
        /// </summary>
        public virtual void CheckDependencies(List<ModEntity> modEntities = null) {
            // 待重启实体未生效：不参与依赖检查，也不被加入 ModList
            if (IsDisabled || modInfo == null || IsPendingRestart) {
                return;
            }
            modEntities ??= ModsManager.ModList;
            if (modInfo.DependencyRanges.Count == 0) {
                IsDependencyChecked = true;
                modEntities.Add(this);
                ModsManager.PackageNameToModEntity.TryAdd(modInfo.PackageName, this);
                return;
            }
            LoadingScreen.Info($"[{modInfo.Name}] Checking dependencies.");
            foreach ((string name, VersionRange range) in modInfo.DependencyRanges) {
                // 排除待重启实体：其内容本进程未加载，当作依赖提供者会让依赖方带缺失依赖放行
                ModEntity entity = ModsManager.ModListAll.Find(px => !px.IsDisabled
                    && !px.IsPendingRestart
                    && px.modInfo != null
                    && px.modInfo.PackageName == name
                    && ((px.modInfo.NuGetVersion != null && range.Satisfies(px.modInfo.NuGetVersion)) || range.Equals(VersionRange.All) || px.modInfo.Version == range.OriginalString)
                );
                if (entity != null) {
                    if (!entity.IsDependencyChecked) {
                        if (entity.modInfo.DependencyRanges.ContainsKey(modInfo.PackageName)) {
                            IsDisabled = true;
                            DisableReason = ModDisableReason.DependencyError;
                            entity.IsDisabled = true;
                            entity.DisableReason = ModDisableReason.DependencyError;
                            Log.Error($"[{modInfo.Name}] Dependency {name} is dependent on {modInfo.Name}, which is not allowed.");
                            return;
                        }
                        entity.CheckDependencies(modEntities);
                    }
                }
                else {
                    IsDisabled = true;
                    DisableReason = ModDisableReason.DependencyError;
                    string versionStr = string.IsNullOrEmpty(range.OriginalString) ? string.Empty : $", Required version: {range}";
                    Log.Error($"[{modInfo.Name}] Failed to find dependency: Package Name \"{name}\"{versionStr}");
                    return;
                }
            }
            IsDependencyChecked = true;
            modEntities.Add(this);
            ModsManager.PackageNameToModEntity.TryAdd(modInfo.PackageName, this);
        }

        /// <summary>
        ///     保存设置
        /// </summary>
        /// <param name="xElement"></param>
        public virtual void SaveSettings(XElement xElement) {
            Loaders.ForEach(item => item.SaveSettings(xElement));
        }

        /// <summary>
        ///     加载设置
        /// </summary>
        /// <param name="xElement"></param>
        public virtual void LoadSettings(XElement xElement) {
            Loaders.ForEach(item => item.LoadSettings(xElement));
        }

        /// <summary>
        ///     BlocksManager初始化完毕
        /// </summary>
        // <param name="categories"></param>
        public virtual void OnBlocksInitalized() {
            Loaders.ForEach(item => item.BlocksInitalized());
        }

        //释放资源
        public virtual void Dispose() {
            try {
                Loaders.ForEach(item => item.ModDispose());
            }
            catch {
                // ignored
            }
            ModArchive?.ZipFileStream.Dispose();
        }

        public override bool Equals(object obj) {
            if (obj is ModEntity px) {
                return px.modInfo.PackageName == modInfo.PackageName
                    && (px.modInfo.NuGetVersion == null
                        ? px.modInfo.Version == modInfo.Version
                        : px.modInfo.NuGetVersion.Equals(modInfo.NuGetVersion));
            }
            return false;
        }

        public override int GetHashCode() =>
            // ReSharper disable NonReadonlyMemberInGetHashCode
            modInfo.GetHashCode();
        // ReSharper restore NonReadonlyMemberInGetHashCode
    }
}