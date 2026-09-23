using Xunit;

namespace Game.Tests {
    public class PackageNameRulesTests {
        [Theory]
        [InlineData("com.studio.hello")]
        [InlineData("com.example.mod2")]
        [InlineData("a.b")]
        public void Accepts_valid_names(string name) {
            Assert.True(PackageNameRules.TryValidate(name, out string error), error);
        }

        [Theory]
        [InlineData("")]
        [InlineData("HelloWorld")]
        [InlineData("com.")]
        [InlineData("1.bad")]
        [InlineData("com.Bad_Name")]
        [InlineData("survivalcraft")]
        [InlineData("fastdebug")]
        [InlineData("com.scspm.modstudio")]
        public void Rejects_invalid_or_reserved(string name) {
            Assert.False(PackageNameRules.TryValidate(name, out string _));
        }

        [Fact]
        public void Suggests_and_assembly_names() {
            Assert.Equal("com.studio.helloworld", PackageNameRules.SuggestPackageName("Hello World"));
            Assert.Equal("HelloWorld", PackageNameRules.ToAssemblyName("Hello World"));
            Assert.Equal("Mod123", PackageNameRules.ToAssemblyName("123"));
            Assert.True(PackageNameRules.IsProtected("com.scspm.modstudio"));
        }
    }
}
