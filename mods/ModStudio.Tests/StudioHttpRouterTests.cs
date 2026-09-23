using Xunit;

namespace Game.Tests {
    public class StudioHttpRouterTests {
        [Fact]
        public void Health_needs_no_auth() {
            Assert.True(StudioHttpRouter.TryMatch("GET", "/health", out StudioRoute route));
            Assert.True(route.IsHealth);
            Assert.False(route.NeedsAuth);
        }

        [Theory]
        [InlineData("GET", "/v1/mods", "mod.list", null)]
        [InlineData("POST", "/v1/mods", "mod.create", null)]
        [InlineData("GET", "/v1/mods/com.studio.hello", "mod.get", "com.studio.hello")]
        [InlineData("DELETE", "/v1/mods/com.studio.hello", "mod.delete_workspace", "com.studio.hello")]
        [InlineData("GET", "/v1/mods/com.studio.hello/files", "fs.list", "com.studio.hello")]
        [InlineData("GET", "/v1/mods/com.studio.hello/file", "fs.read", "com.studio.hello")]
        [InlineData("PUT", "/v1/mods/com.studio.hello/file", "fs.write", "com.studio.hello")]
        [InlineData("POST", "/v1/mods/com.studio.hello/compile", "mod.compile", "com.studio.hello")]
        [InlineData("POST", "/v1/mods/com.studio.hello/pack", "mod.pack", "com.studio.hello")]
        [InlineData("POST", "/v1/mods/com.studio.hello/install", "mod.install", "com.studio.hello")]
        [InlineData("POST", "/v1/mods/com.studio.hello/uninstall", "mod.uninstall", "com.studio.hello")]
        [InlineData("POST", "/v1/game/reload", "game.reload", null)]
        [InlineData("GET", "/v1/game/log", "game.log", null)]
        [InlineData("GET", "/v1/tools", "tools.catalog", null)]
        [InlineData("POST", "/v1/tools/call", "tools.call", null)]
        public void Matches_catalog(string method, string path, string tool, string package) {
            Assert.True(StudioHttpRouter.TryMatch(method, path, out StudioRoute route));
            Assert.Equal(tool, route.ToolName);
            Assert.Equal(package, route.PackageName);
        }

        [Fact]
        public void Unknown_is_rejected() {
            Assert.False(StudioHttpRouter.TryMatch("GET", "/v1/nope", out StudioRoute _));
            Assert.False(StudioHttpRouter.TryMatch("GET", "/v1/mods/com.studio.hello/nope", out StudioRoute _));
        }
    }
}
