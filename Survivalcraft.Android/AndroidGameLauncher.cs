using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Android.App;
using Engine;
using AndroidLog = Android.Util.Log;

namespace SC4Android {
    public static class AndroidGameLauncher {
        const string GameAssemblyName = "Survivalcraft";
        const string LogTag = "SC4Hotload";
        const string UpdateDirectory = "data:Updates/current";
        static readonly object Sync = new();
        static Assembly s_gameAssembly;
        static FieldInfo s_firstFramePreparedField;

        public static bool IsFirstFramePrepared {
            get {
                FieldInfo firstFramePreparedField = s_firstFramePreparedField;
                if (firstFramePreparedField == null) {
                    return true;
                }
                return firstFramePreparedField.GetValue(null) is true;
            }
        }

        public static void Run() {
            lock (Sync) {
                Trace("AndroidGameLauncher.Run begin");
                if (s_gameAssembly != null) {
                    Trace("AndroidGameLauncher.Run skipped, already loaded");
                    return;
                }
                AppDomain.CurrentDomain.AssemblyResolve += ResolveFromUpdateDirectory;
                s_gameAssembly = LoadUpdatedGameAssembly() ?? Assembly.Load(GameAssemblyName);
                Trace($"Loaded game assembly: {s_gameAssembly.FullName}");
                Type programType = s_gameAssembly.GetType("Game.Program", true);
                s_firstFramePreparedField = programType.GetField("m_firstFramePrepared", BindingFlags.Public | BindingFlags.Static);
                MethodInfo entryPoint = programType.GetMethod("EntryPoint", BindingFlags.Public | BindingFlags.Static);
                if (entryPoint == null) {
                    throw new MissingMethodException("Game.Program", "EntryPoint");
                }
                Trace("Invoking Game.Program.EntryPoint");
                entryPoint.Invoke(null, null);
                Trace("Game.Program.EntryPoint returned");
            }
        }

        public static void Trace(string message) {
            try {
                AndroidLog.Error(LogTag, message);
                string filesDir = Application.Context?.FilesDir?.AbsolutePath;
                if (!string.IsNullOrEmpty(filesDir)) {
                    string line = $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}";
                    File.AppendAllText(Path.Combine(filesDir, "hotload.log"), line);
                }
            }
            catch {
                // Diagnostics must not affect startup.
            }
        }

        static Assembly LoadUpdatedGameAssembly() {
            string path = GetUpdateAssemblyPath(GameAssemblyName);
            if (!Storage.FileExists(path)) {
                Trace($"Update assembly not found: {path}");
                return null;
            }
            Trace($"Loading update assembly: {path}");
            byte[] bytes = Storage.ReadAllBytes(path);
            return Assembly.Load(bytes);
        }

        static Assembly ResolveFromUpdateDirectory(object sender, ResolveEventArgs args) {
            string name = new AssemblyName(args.Name).Name;
            if (string.IsNullOrEmpty(name)) {
                return null;
            }
            Assembly loadedAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, name, StringComparison.OrdinalIgnoreCase));
            if (loadedAssembly != null) {
                return loadedAssembly;
            }
            string path = GetUpdateAssemblyPath(name);
            if (!Storage.FileExists(path)) {
                return null;
            }
            Trace($"Resolving dependency from update directory: {path}");
            return Assembly.Load(Storage.ReadAllBytes(path));
        }

        static string GetUpdateAssemblyPath(string assemblyName) =>
            Storage.CombinePaths(UpdateDirectory, $"{assemblyName}.dll");
    }
}
