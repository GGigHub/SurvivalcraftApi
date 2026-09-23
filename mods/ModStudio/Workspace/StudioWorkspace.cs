using System.Text.Json.Nodes;
using Engine;

namespace Game {
    public static class StudioWorkspace {
        public const string FolderName = "ModsStudio";
        public const string ManifestName = ".scmodstudio.json";

        public static string RootStoragePath => Storage.CombinePaths(ModsManager.DocPath, FolderName);

        public static string RootSystemPath {
            get {
                EnsureRoot();
                return Storage.GetSystemPath(RootStoragePath);
            }
        }

        public static void EnsureRoot() {
            if (!Storage.DirectoryExists(ModsManager.DocPath)) {
                Storage.CreateDirectory(ModsManager.DocPath);
            }
            if (!Storage.DirectoryExists(RootStoragePath)) {
                Storage.CreateDirectory(RootStoragePath);
            }
        }

        public static string GetProjectSystemPath(string packageName) => Path.Combine(RootSystemPath, packageName);

        public static bool Exists(string packageName) => Directory.Exists(GetProjectSystemPath(packageName));

        public static List<StudioProjectInfo> ListProjects() {
            EnsureRoot();
            List<StudioProjectInfo> list = [];
            string root = RootSystemPath;
            if (!Directory.Exists(root)) {
                return list;
            }
            foreach (string dir in Directory.GetDirectories(root)) {
                StudioProjectInfo info = TryRead(dir);
                if (info != null && !string.IsNullOrEmpty(info.PackageName)) {
                    list.Add(info);
                }
            }
            list.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        public static StudioProjectInfo TryRead(string projectDir) {
            string manifest = Path.Combine(projectDir, ManifestName);
            if (!File.Exists(manifest)) {
                return null;
            }
            try {
                JsonNode node = JsonNode.Parse(File.ReadAllText(manifest));
                return StudioProjectInfo.FromJson(node);
            }
            catch (Exception e) {
                Log.Error($"[ModStudio] Read manifest failed: {e.Message}");
                return null;
            }
        }

        public static StudioProjectInfo Require(string packageName) {
            if (!PackageNameRules.TryValidate(packageName, out string error) && !PackageNameRules.IsProtected(packageName)) {
                throw new InvalidOperationException(error);
            }
            string dir = GetProjectSystemPath(packageName);
            StudioProjectInfo info = TryRead(dir);
            if (info == null) {
                throw new FileNotFoundException("Workspace not found.", dir);
            }
            return info;
        }

        public static void Save(StudioProjectInfo info) {
            string dir = GetProjectSystemPath(info.PackageName);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, ManifestName), info.ToJson().ToJsonString());
        }

        public static void DeleteProject(string packageName) {
            string dir = GetProjectSystemPath(packageName);
            if (Directory.Exists(dir)) {
                Directory.Delete(dir, true);
            }
        }

        public static string FindCsproj(string packageName) {
            string dir = GetProjectSystemPath(packageName);
            string[] files = Directory.GetFiles(dir, "*.csproj", SearchOption.TopDirectoryOnly);
            if (files.Length == 0) {
                throw new FileNotFoundException("No csproj in workspace.", dir);
            }
            return files[0];
        }

        public static string OutputDirectory(StudioProjectInfo info) =>
            Path.Combine(GetProjectSystemPath(info.PackageName), "bin", "Release");

        public static string PackOutputPath(StudioProjectInfo info) =>
            Path.Combine(GetProjectSystemPath(info.PackageName), "out", info.AssemblyName + ".scmod");
    }
}
