using System.Text.Json;
using System.Xml.Linq;
using Game.Network;
using Survivalcraft.Testing.Control;
using Xunit;

namespace Survivalcraft.Testing.Tests;

public sealed class ReleaseContractTests {
    [Fact]
    [Trait("Layer", "L0")]
    public void MultiplayerPreviewMetadataIsConsistent() {
        string root = FindRepositoryRoot();
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(root, "release", "multiplayer-preview.json")));
        JsonElement release = document.RootElement;

        Assert.Equal(1, release.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("API_1.9.3.1_MP-preview.1", release.GetProperty("tag").GetString());
        Assert.Equal("1.9.3.1-mp-preview.1", release.GetProperty("releaseVersion").GetString());
        Assert.Equal("1.9.3.1", release.GetProperty("apiVersion").GetString());
        Assert.Equal(NetworkCompatibility.ProtocolRevision, release.GetProperty("protocolRevision").GetInt32());
        Assert.Equal(AutomationProtocol.CurrentApiVersion, release.GetProperty("automationApiVersion").GetInt32());
        Assert.Equal(AutomationProtocol.CurrentSnapshotSchemaVersion, release.GetProperty("snapshotSchemaVersion").GetInt32());
        Assert.True(release.GetProperty("prerelease").GetBoolean());

        JsonElement android = release.GetProperty("android");
        Assert.Equal("android-arm64", android.GetProperty("runtimeIdentifier").GetString());
        Assert.Equal("arm64-v8a", android.GetProperty("abi").GetString());
        Assert.Equal(19311, android.GetProperty("versionCode").GetInt32());
        Assert.Equal("API1.9.3.1-MP-preview.1", android.GetProperty("versionName").GetString());
        Assert.Equal("development", android.GetProperty("signing").GetString());

        XDocument common = XDocument.Load(Path.Combine(root, "build", "Common.props"));
        Assert.Equal("1.9.3.1", Property(common, "Version"));
        XDocument multiplayerRelease = XDocument.Load(
            Path.Combine(root, "build", "MultiplayerRelease.props"));
        Assert.Equal(release.GetProperty("releaseVersion").GetString(),
            Property(multiplayerRelease, "MultiplayerReleaseVersion"));
        Assert.Equal(release.GetProperty("tag").GetString(),
            Property(multiplayerRelease, "MultiplayerReleaseTag"));

        AssertModInfo(root, Path.Combine("Survivalcraft", "Content", "modinfo.json"),
            version: "1.9.3.1", apiVersion: "1.9.3.1");
        AssertModInfo(root, Path.Combine("Survivalcraft.Multiplayer", "modinfo.json"),
            version: "1.9.3.1-mp-preview.1", apiVersion: "1.9.3.1");
        AssertModInfo(root, Path.Combine("Survivalcraft.TestAutomation", "modinfo.json"),
            version: "1.9.3.1-mp-preview.1", apiVersion: "1.9.3.1");

        string modsManager = File.ReadAllText(
            Path.Combine(root, "Survivalcraft", "ModsManager", "ModsManager.cs"));
        Assert.Matches("APIVersionString\\s*=\\s*\"1\\.9\\.3\\.1\"", modsManager);
        Assert.Matches("APINuGetVersion\\s*=\\s*new\\(1,\\s*9,\\s*3,\\s*1\\)", modsManager);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void AndroidAndArtifactContractIsArm64OnlyAndPublic() {
        string root = FindRepositoryRoot();
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(root, "release", "multiplayer-preview.json")));
        JsonElement release = document.RootElement;
        JsonElement artifacts = release.GetProperty("artifacts");

        Assert.Equal("Survivalcraft-API-1.9.3.1-MP-preview.1-Windows-x64.zip",
            artifacts.GetProperty("windowsX64").GetString());
        Assert.Equal("Survivalcraft-RelayServer-1.9.3.1-MP-preview.1-win-x64.zip",
            artifacts.GetProperty("relayWindowsX64").GetString());
        Assert.Equal("Survivalcraft-API-1.9.3.1-MP-preview.1-Android-arm64.apk",
            artifacts.GetProperty("androidArm64").GetString());
        Assert.DoesNotContain("TestAutomation", string.Join('\n',
            artifacts.EnumerateObject().Select(property => property.Value.GetString())));

        XDocument androidProps = XDocument.Load(Path.Combine(root, "build", "Android.props"));
        Assert.Equal("android-arm64", Property(androidProps, "RuntimeIdentifier"));

        XDocument manifest = XDocument.Load(
            Path.Combine(root, "Survivalcraft.Android", "Properties", "AndroidManifest.xml"));
        XNamespace androidNamespace = "http://schemas.android.com/apk/res/android";
        Assert.Equal(release.GetProperty("android").GetProperty("versionCode").GetInt32().ToString(),
            manifest.Root?.Attribute(androidNamespace + "versionCode")?.Value);
        Assert.Equal(release.GetProperty("android").GetProperty("versionName").GetString(),
            manifest.Root?.Attribute(androidNamespace + "versionName")?.Value);
    }

    private static string Property(XDocument document, string name) =>
        document.Descendants(name).Single().Value;

    private static void AssertModInfo(
        string root,
        string relativePath,
        string version,
        string apiVersion) {
        using JsonDocument document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root, relativePath)));
        Assert.Equal(version, document.RootElement.GetProperty("Version").GetString());
        Assert.Equal(apiVersion, document.RootElement.GetProperty("ApiVersion").GetString());
    }

    private static string FindRepositoryRoot() {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null) {
            if (File.Exists(Path.Combine(directory.FullName, "SurvivalcraftApi.sln"))) {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
