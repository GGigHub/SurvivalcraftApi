using System.IO.Compression;
using Xunit;

namespace Game.Tests {
    public class ModPackerTests {
        [Fact]
        public void Packs_dll_modinfo_and_assets_without_sources() {
            string root = Path.Combine(Path.GetTempPath(), "modstudio-pack-" + Guid.NewGuid().ToString("N"));
            string output = Path.Combine(root, "bin");
            Directory.CreateDirectory(Path.Combine(output, "Assets", "Lang"));
            File.WriteAllText(Path.Combine(output, "Hello.dll"), "dll");
            File.WriteAllText(Path.Combine(output, "modinfo.json"), "{\"Name\":\"Hello\"}");
            File.WriteAllText(Path.Combine(output, "Assets", "Lang", "en-US.json"), "{}");
            File.WriteAllText(Path.Combine(output, "Hello.pdb"), "skip");
            string dest = Path.Combine(root, "out", "Hello.scmod");
            try {
                ModPacker.PackOutputDirectory(output, "Hello", dest);
                Assert.True(File.Exists(dest));
                using ZipArchive zip = ZipFile.OpenRead(dest);
                HashSet<string> names = zip.Entries.Select(e => e.FullName.Replace('\\', '/')).ToHashSet();
                Assert.Contains("Hello.dll", names);
                Assert.Contains("modinfo.json", names);
                Assert.Contains("Assets/Lang/en-US.json", names);
                Assert.DoesNotContain("Hello.pdb", names);
                Assert.DoesNotContain(names, n => n.EndsWith(".cs"));
            }
            finally {
                Directory.Delete(root, true);
            }
        }
    }
}
