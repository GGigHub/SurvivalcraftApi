namespace Game {
    public static class ModPacker {
        public static void PackOutputDirectory(string outputDir, string assemblyName, string destScmodPath) {
            if (string.IsNullOrWhiteSpace(outputDir) || string.IsNullOrWhiteSpace(assemblyName) || string.IsNullOrWhiteSpace(destScmodPath)) {
                throw new ArgumentException("Pack arguments are empty.");
            }
            string dllPath = Path.Combine(outputDir, assemblyName + ".dll");
            string infoPath = Path.Combine(outputDir, "modinfo.json");
            if (!File.Exists(dllPath)) {
                throw new FileNotFoundException("Compiled dll was not found.", dllPath);
            }
            if (!File.Exists(infoPath)) {
                throw new FileNotFoundException("modinfo.json was not found.", infoPath);
            }
            string destDir = Path.GetDirectoryName(destScmodPath);
            if (!string.IsNullOrEmpty(destDir)) {
                Directory.CreateDirectory(destDir);
            }
            if (File.Exists(destScmodPath)) {
                File.Delete(destScmodPath);
            }
            using FileStream stream = File.Create(destScmodPath);
            using System.IO.Compression.ZipArchive zip = new(stream, System.IO.Compression.ZipArchiveMode.Create);
            AddFile(zip, dllPath, assemblyName + ".dll");
            AddFile(zip, infoPath, "modinfo.json");
            TryAdd(zip, Path.Combine(outputDir, "icon.png"), "icon.png");
            TryAdd(zip, Path.Combine(outputDir, "icon.webp"), "icon.webp");
            string assetsDir = Path.Combine(outputDir, "Assets");
            if (Directory.Exists(assetsDir)) {
                foreach (string file in Directory.GetFiles(assetsDir, "*", SearchOption.AllDirectories)) {
                    string relative = Path.GetRelativePath(outputDir, file).Replace('\\', '/');
                    AddFile(zip, file, relative);
                }
            }
        }

        static void TryAdd(System.IO.Compression.ZipArchive zip, string sourcePath, string entryName) {
            if (File.Exists(sourcePath)) {
                AddFile(zip, sourcePath, entryName);
            }
        }

        static void AddFile(System.IO.Compression.ZipArchive zip, string sourcePath, string entryName) {
            System.IO.Compression.ZipArchiveEntry entry = zip.CreateEntry(
                entryName.Replace('\\', '/'),
                System.IO.Compression.CompressionLevel.Optimal
            );
            using Stream dest = entry.Open();
            using FileStream source = File.OpenRead(sourcePath);
            source.CopyTo(dest);
        }
    }
}
