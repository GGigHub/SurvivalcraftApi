using System.Text.Json;
using System.Text.RegularExpressions;
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

    [Fact]
    [Trait("Layer", "L0")]
    public void AndroidDeviceAutomationIsOptInAndPublicBuildFailsClosed() {
        string root = FindRepositoryRoot();
        string androidProjectPath = Path.Combine(
            root, "Survivalcraft.Android", "Survivalcraft.Android.csproj");
        XDocument androidProject = XDocument.Load(androidProjectPath);
        XElement defaultSwitch = androidProject.Descendants("EnableAndroidDeviceTestAutomation")
            .Single(element => (string?)element.Attribute("Condition") is { } condition
                && condition.Contains("== ''", StringComparison.Ordinal));
        Assert.Equal("false", defaultSwitch.Value.Trim());

        XElement testAssetGroup = androidProject.Descendants("ItemGroup")
            .Single(element => (string?)element.Attribute("Condition") is { } condition
                && condition.Contains("EnableAndroidDeviceTestAutomation", StringComparison.Ordinal)
                && element.Descendants("AndroidAsset").Any(asset =>
                    asset.Value.Contains("AndroidTestAutomation", StringComparison.Ordinal)));
        Assert.Contains("== 'true'", (string?)testAssetGroup.Attribute("Condition") ?? string.Empty,
            StringComparison.Ordinal);

        string activitySource = File.ReadAllText(
            Path.Combine(root, "Survivalcraft.Android", "MainActivity.cs"));
        Assert.Contains("#if ANDROID_DEVICE_TEST_AUTOMATION", activitySource,
            StringComparison.Ordinal);
        Assert.Contains("android:Survivalcraft2.4_API1.9_AutoTest", activitySource,
            StringComparison.Ordinal);

        string testBuild = File.ReadAllText(
            Path.Combine(root, "scripts", "Build-AndroidDeviceTest.ps1"));
        Assert.Contains("com.candy.survivalcraftAPI1_9.autotest", testBuild,
            StringComparison.Ordinal);
        Assert.Contains("-p:EnableAndroidDeviceTestAutomation=true", testBuild,
            StringComparison.Ordinal);
        Assert.Contains("Survivalcraft2.4_API1.9_AutoTest", testBuild,
            StringComparison.Ordinal);

        string publicBuild = File.ReadAllText(
            Path.Combine(root, "scripts", "Build-MultiplayerPreview.ps1"));
        Assert.Contains("-p:EnableAndroidDeviceTestAutomation=false", publicBuild,
            StringComparison.Ordinal);
        Assert.Contains("Public Android package contains test automation assets", publicBuild,
            StringComparison.Ordinal);
        string publicVerifier = File.ReadAllText(
            Path.Combine(root, "scripts", "Test-MultiplayerPreviewRelease.ps1"));
        Assert.Contains("Public APK contains test automation assets", publicVerifier,
            StringComparison.Ordinal);

        string bridgeSource = File.ReadAllText(Path.Combine(
            root,
            "Survivalcraft.AndroidTestAutomation",
            "AndroidDeviceTestAutomationModLoader.cs"));
        Assert.Contains("\"system.ping\"", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("\"mods.list\"", bridgeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("CSharpScript", bridgeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Process.Start", bridgeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Assembly.Load", bridgeSource, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void AndroidCommunityModCorpusIsHashLockedAndHasSingleModCoverage() {
        string root = FindRepositoryRoot();
        string corpusDirectory = Path.Combine(root, "tests", "android-mod-corpus");
        using JsonDocument manifestDocument = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(corpusDirectory, "manifest.json")));
        using JsonDocument lockDocument = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(corpusDirectory, "lock.json")));
        JsonElement manifest = manifestDocument.RootElement;
        JsonElement corpusLock = lockDocument.RootElement;
        Assert.Equal(1, manifest.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(1, corpusLock.GetProperty("schemaVersion").GetInt32());

        HashSet<string> allowedHosts = manifest.GetProperty("allowedHosts")
            .EnumerateArray()
            .Select(value => value.GetString() ?? string.Empty)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.NotEmpty(allowedHosts);

        Dictionary<string, JsonElement> lockedById = corpusLock.GetProperty("mods")
            .EnumerateArray()
            .ToDictionary(
                mod => mod.GetProperty("id").GetString() ?? string.Empty,
                mod => mod,
                StringComparer.OrdinalIgnoreCase);
        JsonElement[] enabledMods = manifest.GetProperty("mods")
            .EnumerateArray()
            .Where(mod => !mod.TryGetProperty("enabled", out JsonElement enabled)
                || enabled.GetBoolean())
            .ToArray();
        Assert.Equal(enabledMods.Length, lockedById.Count);

        HashSet<string> manifestIds = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement mod in enabledMods) {
            string id = mod.GetProperty("id").GetString() ?? string.Empty;
            Assert.Matches("^[a-z0-9][a-z0-9._-]{0,63}$", id);
            Assert.True(manifestIds.Add(id), $"Duplicate corpus id '{id}'.");
            string sourceUrl = mod.GetProperty("sourceUrl").GetString() ?? string.Empty;
            Assert.True(Uri.TryCreate(sourceUrl, UriKind.Absolute, out Uri? uri));
            Assert.Equal(Uri.UriSchemeHttps, uri!.Scheme);
            Assert.Contains(uri.Host, allowedHosts);

            Assert.True(lockedById.TryGetValue(id, out JsonElement locked),
                $"Corpus lock is missing '{id}'.");
            Assert.Equal(sourceUrl, locked.GetProperty("sourceUrl").GetString());
            Assert.Equal(mod.GetProperty("expectedBytes").GetInt64(),
                locked.GetProperty("bytes").GetInt64());
            Assert.Equal(mod.GetProperty("expectedPackageName").GetString(),
                locked.GetProperty("packageName").GetString());
            Assert.Equal(mod.GetProperty("expectedVersion").GetString(),
                locked.GetProperty("version").GetString());
            Assert.Matches("^[0-9a-f]{64}$",
                locked.GetProperty("sha256").GetString() ?? string.Empty);
            Assert.InRange(locked.GetProperty("archive").GetProperty("entryCount").GetInt32(),
                1,
                manifest.GetProperty("limits").GetProperty("maximumEntries").GetInt32());
            Assert.InRange(locked.GetProperty("archive").GetProperty("expandedBytes").GetInt64(),
                1,
                manifest.GetProperty("limits").GetProperty("maximumExpandedBytes").GetInt64());
        }

        HashSet<string> caseIds = new(StringComparer.OrdinalIgnoreCase);
        List<string[]> cases = [];
        foreach (JsonElement testCase in manifest.GetProperty("testCases").EnumerateArray()) {
            string caseId = testCase.GetProperty("id").GetString() ?? string.Empty;
            Assert.True(caseIds.Add(caseId), $"Duplicate corpus case '{caseId}'.");
            Assert.Contains(testCase.GetProperty("mode").GetString() ?? string.Empty,
                new[] { "doctor", "relay-reentry-pad" });
            Assert.True(testCase.GetProperty("gate").GetBoolean());
            string[] modIds = testCase.GetProperty("mods").EnumerateArray()
                .Select(value => value.GetString() ?? string.Empty)
                .ToArray();
            Assert.NotEmpty(modIds);
            Assert.Equal(modIds.Length, modIds.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            foreach (string modId in modIds) {
                Assert.Contains(modId, manifestIds);
            }
            cases.Add(modIds);
        }
        Assert.Contains(cases, testCase => testCase.Length > 1);
        foreach (string modId in manifestIds) {
            Assert.Single(cases.Where(testCase => testCase.Length == 1
                && string.Equals(testCase[0], modId, StringComparison.OrdinalIgnoreCase)));
        }
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
