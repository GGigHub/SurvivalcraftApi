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
        string deviceRunner = File.ReadAllText(
            Path.Combine(root, "scripts", "Test-AndroidDevice.ps1"));
        Assert.Contains("Bugs/Game.log", deviceRunner, StringComparison.Ordinal);
        Assert.Contains("runtimeLogAudit", deviceRunner, StringComparison.Ordinal);
        Assert.Contains("World loading returned to the main menu with a MessageDialog",
            deviceRunner,
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
        Assert.Contains("\"mods.selectClassSubstitute\"", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("startup-class-substitutes.json", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("selectedClassSubstitutes", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("loadingCanContinue", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("screenName", bridgeSource, StringComparison.Ordinal);
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

        string corpusResolver = File.ReadAllText(
            Path.Combine(root, "scripts", "Resolve-AndroidModCorpus.ps1"));
        Assert.Contains("\"--globoff\"", corpusResolver, StringComparison.Ordinal);

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
        Assert.True(enabledMods.Length >= 8,
            "The Android corpus must retain a representative set of real API mods.");
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
            Assert.Matches("^1\\.9(?:\\.|$)",
                locked.GetProperty("apiVersion").GetString() ?? string.Empty);
            Assert.StartsWith("2.4", locked.GetProperty("scVersion").GetString() ?? string.Empty,
                StringComparison.Ordinal);
            Assert.InRange(locked.GetProperty("archive").GetProperty("entryCount").GetInt32(),
                1,
                manifest.GetProperty("limits").GetProperty("maximumEntries").GetInt32());
            Assert.InRange(locked.GetProperty("archive").GetProperty("expandedBytes").GetInt64(),
                1,
                manifest.GetProperty("limits").GetProperty("maximumExpandedBytes").GetInt64());
            Assert.Contains(".dll",
                locked.GetProperty("archive").GetProperty("extensions")
                    .EnumerateArray()
                    .Select(extension => extension.GetString() ?? string.Empty),
                StringComparer.OrdinalIgnoreCase);
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

    [Fact]
    [Trait("Layer", "L0")]
    public void AndroidClassicModCorpusIsHashLockedAndModelsDependencyAwareSubjects() {
        string root = FindRepositoryRoot();
        string corpusDirectory = Path.Combine(root, "tests", "android-classic-mod-corpus");
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
        Assert.Equal(6, enabledMods.Length);
        Assert.Equal(enabledMods.Length, lockedById.Count);

        HashSet<string> manifestIds = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement mod in enabledMods) {
            string id = mod.GetProperty("id").GetString() ?? string.Empty;
            Assert.True(manifestIds.Add(id), $"Duplicate classic corpus id '{id}'.");
            string sourceUrl = mod.GetProperty("sourceUrl").GetString() ?? string.Empty;
            Assert.True(Uri.TryCreate(sourceUrl, UriKind.Absolute, out Uri? uri));
            Assert.Equal(Uri.UriSchemeHttps, uri!.Scheme);
            Assert.Contains(uri.Host, allowedHosts);
            Assert.True(lockedById.TryGetValue(id, out JsonElement locked),
                $"Classic corpus lock is missing '{id}'.");
            Assert.Equal(sourceUrl, locked.GetProperty("sourceUrl").GetString());
            Assert.Equal(mod.GetProperty("expectedBytes").GetInt64(),
                locked.GetProperty("bytes").GetInt64());
            Assert.Equal(mod.GetProperty("expectedPackageName").GetString(),
                locked.GetProperty("packageName").GetString());
            Assert.Equal(mod.GetProperty("expectedVersion").GetString(),
                locked.GetProperty("version").GetString());
            Assert.Matches("^[0-9a-f]{64}$",
                locked.GetProperty("sha256").GetString() ?? string.Empty);
            string[] extensions = locked.GetProperty("archive").GetProperty("extensions")
                .EnumerateArray()
                .Select(extension => extension.GetString() ?? string.Empty)
                .ToArray();
            if (id == "siyah-akrep-2.8") {
                Assert.DoesNotContain(".dll", extensions, StringComparer.OrdinalIgnoreCase);
                Assert.Contains(".xdb", extensions, StringComparer.OrdinalIgnoreCase);
            }
            else {
                Assert.Contains(".dll", extensions, StringComparer.OrdinalIgnoreCase);
            }
        }

        foreach (string id in new[] {
            "bldg-1.2.1",
            "accessory-1.2.1",
            "wonderful-era-1.9.2a"
        }) {
            Assert.Matches("^1\\.9(?:\\.|$)",
                lockedById[id].GetProperty("apiVersion").GetString() ?? string.Empty);
        }
        foreach (string id in new[] {
            "dayz-2.2.3.0-three-year",
            "firearms-2.8",
            "siyah-akrep-2.8"
        }) {
            Assert.Equal("1.45", lockedById[id].GetProperty("apiVersion").GetString());
        }

        JsonElement[] testCases = manifest.GetProperty("testCases").EnumerateArray().ToArray();
        Assert.Equal(enabledMods.Length, testCases.Length);
        foreach (JsonElement testCase in testCases) {
            Assert.False(testCase.GetProperty("gate").GetBoolean());
            string subject = testCase.GetProperty("subjectMod").GetString() ?? string.Empty;
            string[] modIds = testCase.GetProperty("mods").EnumerateArray()
                .Select(value => value.GetString() ?? string.Empty)
                .ToArray();
            Assert.Contains(subject, modIds, StringComparer.OrdinalIgnoreCase);
            Assert.Contains(subject, manifestIds);
        }
        foreach (string id in manifestIds) {
            Assert.Single(testCases.Where(testCase => string.Equals(
                testCase.GetProperty("subjectMod").GetString(),
                id,
                StringComparison.OrdinalIgnoreCase)));
        }

        JsonElement wonderfulEraCase = testCases.Single(testCase =>
            testCase.GetProperty("id").GetString() == "wonderful-era-with-dependencies");
        string[] wonderfulEraMods = wonderfulEraCase.GetProperty("mods").EnumerateArray()
            .Select(value => value.GetString() ?? string.Empty)
            .ToArray();
        Assert.Contains("bldg-1.2.1", wonderfulEraMods);
        Assert.Contains("accessory-1.2.1", wonderfulEraMods);
        JsonElement dayzCase = testCases.Single(testCase =>
            testCase.GetProperty("id").GetString() == "dayz-subject");
        JsonElement[] dayzSelections = dayzCase.GetProperty("classSubstitutions")
            .EnumerateArray()
            .ToArray();
        Assert.Equal(6, dayzSelections.Length);
        Assert.Contains(dayzSelections, selection =>
            selection.GetProperty("guid").GetString() == "dafb8e14-11b9-44b7-a208-424b770aeaa9"
            && selection.GetProperty("packageName").GetString() == "DayZ"
            && selection.GetProperty("className").GetString() == "Game.Subsystem新抛射体行为");
        JsonElement dayzDialogAction = Assert.Single(
            dayzCase.GetProperty("startupDialogActions").EnumerateArray());
        Assert.Equal("BulletinDialog",
            dayzDialogAction.GetProperty("dialogType").GetString());
        Assert.Equal("OkButton", dayzDialogAction.GetProperty("query").GetString());
        JsonElement siyahAkrepCase = testCases.Single(testCase =>
            testCase.GetProperty("id").GetString() == "siyah-akrep-with-firearms");
        Assert.Contains("firearms-2.8", siyahAkrepCase.GetProperty("mods")
            .EnumerateArray()
            .Select(value => value.GetString() ?? string.Empty));
        foreach (JsonElement testCase in testCases.Where(testCase =>
            testCase.GetProperty("id").GetString() is "firearms-subject" or "siyah-akrep-with-firearms")) {
            Dictionary<string, JsonElement> selections = testCase
                .GetProperty("classSubstitutions")
                .EnumerateArray()
                .ToDictionary(
                    selection => selection.GetProperty("guid").GetString() ?? string.Empty,
                    selection => selection,
                    StringComparer.OrdinalIgnoreCase);
            bool isSiyahAkrep = testCase.GetProperty("id").GetString()
                == "siyah-akrep-with-firearms";
            Assert.Equal(isSiyahAkrep ? 3 : 2, selections.Count);
            Assert.Equal("gunsmod.ysf.scmod",
                selections["dafb8e14-11b9-44b7-a208-424b770aeaa9"]
                    .GetProperty("packageName").GetString());
            Assert.Equal("Game.SubsystemNewProjectiles",
                selections["dafb8e14-11b9-44b7-a208-424b770aeaa9"]
                    .GetProperty("className").GetString());
            Assert.Equal("gunsmod.ysf.scmod",
                selections["96e79f99-a082-4190-9ab6-835dc49ebbdd"]
                    .GetProperty("packageName").GetString());
            Assert.Equal("Game.SubsystemNewExplosions",
                selections["96e79f99-a082-4190-9ab6-835dc49ebbdd"]
                    .GetProperty("className").GetString());
            if (isSiyahAkrep) {
                Assert.Equal("siyahakrep.ysf.scmod",
                    selections["1df4e627-c959-4e6a-bfa2-b7ee3ef08c99"]
                        .GetProperty("packageName").GetString());
                Assert.Equal("Game.ComponentSAClothing",
                    selections["1df4e627-c959-4e6a-bfa2-b7ee3ef08c99"]
                        .GetProperty("className").GetString());
            }
        }

        string matrixRunner = File.ReadAllText(
            Path.Combine(root, "scripts", "Test-AndroidModMatrix.ps1"));
        Assert.Contains("subjectMod", matrixRunner, StringComparison.Ordinal);
        Assert.Contains("subjectModId", matrixRunner, StringComparison.Ordinal);
        Assert.Contains("MaximumModPackageBytes", matrixRunner, StringComparison.Ordinal);
        Assert.Contains("classSubstitutions", matrixRunner, StringComparison.Ordinal);
        Assert.Contains("startupDialogActions", matrixRunner, StringComparison.Ordinal);
        Assert.Contains("failedObservationalCaseCount", matrixRunner, StringComparison.Ordinal);
        Assert.Contains("allCasesPassed", matrixRunner, StringComparison.Ordinal);

        string contentManager = File.ReadAllText(
            Path.Combine(root, "Survivalcraft", "Managers", "ContentManager.cs"));
        string modEntity = File.ReadAllText(
            Path.Combine(root, "Survivalcraft", "ModsManager", "ModEntity.cs"));
        string fastDebugModEntity = File.ReadAllText(
            Path.Combine(root, "Survivalcraft", "ModsManager", "FastDebugModEntity.cs"));
        string bitmapFontReader = File.ReadAllText(
            Path.Combine(root, "Survivalcraft", "IContentReader", "BitmapFontReader.cs"));
        Assert.Contains("SourcePackageName", contentManager, StringComparison.Ordinal);
        Assert.Contains("new(filename.Substring(7), modInfo.PackageName)", modEntity,
            StringComparison.Ordinal);
        Assert.Contains("modInfo?.PackageName ?? string.Empty", fastDebugModEntity,
            StringComparison.Ordinal);
        Assert.Contains("sameSourceTextures", bitmapFontReader, StringComparison.Ordinal);
        Assert.Contains("glyphs.SourcePackageName", bitmapFontReader, StringComparison.Ordinal);
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
