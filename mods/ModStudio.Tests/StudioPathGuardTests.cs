using Xunit;

namespace Game.Tests {
    public class StudioPathGuardTests {
        [Fact]
        public void Resolves_relative_file() {
            string root = Path.Combine(Path.GetTempPath(), "modstudio-guard-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try {
                Assert.True(StudioPathGuard.TryResolve(root, "Mod/A.cs", out string full, out string error));
                Assert.Null(error);
                Assert.StartsWith(Path.GetFullPath(root), full);
            }
            finally {
                Directory.Delete(root, true);
            }
        }

        [Theory]
        [InlineData("../secret.txt")]
        [InlineData("..\\secret.txt")]
        [InlineData("/etc/passwd")]
        public void Rejects_escape(string relative) {
            string root = Path.Combine(Path.GetTempPath(), "modstudio-guard-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try {
                Assert.False(StudioPathGuard.TryResolve(root, relative, out string _, out string code));
                Assert.Equal("path_escape", code);
            }
            finally {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void Rejects_windows_absolute() {
            if (!OperatingSystem.IsWindows()) {
                return;
            }
            string root = Path.Combine(Path.GetTempPath(), "modstudio-guard-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try {
                Assert.False(StudioPathGuard.TryResolve(root, @"C:\Windows\win.ini", out string _, out string code));
                Assert.Equal("path_escape", code);
            }
            finally {
                Directory.Delete(root, true);
            }
        }
    }
}
