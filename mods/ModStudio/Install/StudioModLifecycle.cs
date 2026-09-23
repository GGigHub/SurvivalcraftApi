using Engine;

namespace Game {
    public static class StudioModLifecycle {
        public static bool IsProtected(string packageName) => PackageNameRules.IsProtected(packageName);

        public static string Install(StudioProjectInfo info) {
            if (info == null) {
                throw new ArgumentNullException(nameof(info));
            }
            if (IsProtected(info.PackageName)) {
                throw new InvalidOperationException("Protected package cannot be installed by ModStudio.");
            }
            string scmod = StudioWorkspace.PackOutputPath(info);
            if (!File.Exists(scmod)) {
                throw new FileNotFoundException("Packed scmod not found. Pack first.", scmod);
            }
            DeleteInstalledFile(info.PackageName, info.AssemblyName);
            using FileStream stream = File.OpenRead(scmod);
            return ModsManager.ImportMod(info.AssemblyName, stream);
        }

        public static void Uninstall(string packageName, string assemblyName) {
            if (IsProtected(packageName)) {
                throw new InvalidOperationException("Protected package cannot be uninstalled by ModStudio.");
            }
            DeleteInstalledFile(packageName, assemblyName);
        }

        public static bool IsInstalled(string packageName) {
            foreach (ModEntity entity in ModsManager.ModListAll) {
                if (entity?.modInfo != null && entity.modInfo.PackageName == packageName && !string.IsNullOrEmpty(entity.ModFilePath)) {
                    return Storage.FileExists(entity.ModFilePath);
                }
            }
            return false;
        }

        public static void RequestReload() {
            void Reload() {
                try {
                    SettingsManager.SaveSettings();
                    ModEntity[] mods = ModsManager.ModList.ToArray();
                    foreach (ModEntity mod in mods) {
                        try {
                            mod.Dispose();
                        }
                        catch (Exception e) {
                            Log.Error($"[ModStudio] Dispose failed: {e}");
                        }
                    }
                    ScreensManager.SwitchScreen("Loading");
                }
                catch (Exception e) {
                    Log.Error($"[ModStudio] Reload failed: {e}");
                }
            }
            Dispatcher.Dispatch(Reload);
        }

        static void DeleteInstalledFile(string packageName, string assemblyName) {
            foreach (ModEntity entity in ModsManager.ModListAll) {
                if (entity?.modInfo == null || entity.modInfo.PackageName != packageName) {
                    continue;
                }
                if (entity is SurvivalCraftModEntity || entity is FastDebugModEntity) {
                    throw new InvalidOperationException("Protected package cannot be replaced.");
                }
                if (!string.IsNullOrEmpty(entity.ModFilePath) && Storage.FileExists(entity.ModFilePath)) {
                    Storage.DeleteFile(entity.ModFilePath);
                }
            }
            if (!string.IsNullOrEmpty(assemblyName)) {
                string fallback = Storage.CombinePaths(ModsManager.ModsPath, assemblyName + ModsManager.ModSuffix);
                if (Storage.FileExists(fallback)) {
                    Storage.DeleteFile(fallback);
                }
            }
        }
    }
}
