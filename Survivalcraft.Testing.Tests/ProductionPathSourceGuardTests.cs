using System.Text.RegularExpressions;
using Xunit;

namespace Survivalcraft.Testing.Tests;

public sealed class ProductionPathSourceGuardTests {
    private static readonly Regex DirectQueue = new(
        @"\bNetworkManager\s*\.\s*Queue\s*\(",
        RegexOptions.CultureInvariant);

    [Fact]
    [Trait("Layer", "L0")]
    public void TestAutomationCannotBypassClassifiedNetworkGateway() {
        string root = FindRepositoryRoot();
        string automation = Path.Combine(root, "Survivalcraft.TestAutomation");
        string gateway = Path.GetFullPath(
            Path.Combine(automation, "ProductionPathGateway.cs"));
        string[] sources = Directory.GetFiles(
            automation,
            "*.cs",
            SearchOption.AllDirectories);

        (string Path, int Count)[] directUses = sources
            .Select(path => (
                Path: Path.GetFullPath(path),
                Count: DirectQueue.Matches(File.ReadAllText(path)).Count))
            .Where(item => item.Count > 0)
            .ToArray();

        (string Path, int Count) only = Assert.Single(directUses);
        Assert.Equal(gateway, only.Path, ignoreCase: true);
        Assert.Equal(1, only.Count);
        string gatewaySource = File.ReadAllText(gateway);
        foreach (string classifiedMethod in new[] {
                     "QueueFixtureSynchronization",
                     "QueueProductionRequest",
                     "QueueAuthoritativeState",
                     "QueueFaultInjection",
                     "QueueFaultInjectionAfterSettle"
                 }) {
            int totalReferences = sources.Sum(path => Regex.Matches(
                File.ReadAllText(path),
                $@"\b{classifiedMethod}\s*\(",
                RegexOptions.CultureInvariant).Count);
            Assert.True(
                totalReferences > 1,
                $"Classified gateway '{classifiedMethod}' has no production call sites.");
        }

        foreach (string path in sources.Where(path =>
                     !string.Equals(Path.GetFullPath(path), gateway, StringComparison.OrdinalIgnoreCase))) {
            string source = File.ReadAllText(path);
            Assert.DoesNotContain("QueueClassifiedPacket(", source, StringComparison.Ordinal);
            Assert.DoesNotContain("NetworkManager.QueueAction", source, StringComparison.Ordinal);
        }
        Assert.Contains("ArgumentNullException.ThrowIfNull(packet)", gatewaySource, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void RuntimeHarnessCannotRegressToForegroundGameWindows() {
        string root = FindRepositoryRoot();
        string gameNodeSource = File.ReadAllText(Path.Combine(
            root,
            "Survivalcraft.Testing",
            "Processes",
            "GameNodeProcess.cs"));
        string relaySource = File.ReadAllText(Path.Combine(
            root,
            "Survivalcraft.Testing",
            "Processes",
            "RelayServerProcess.cs"));

        Assert.Contains("UseShellExecute = false", gameNodeSource, StringComparison.Ordinal);
        Assert.Contains("CreateNoWindow = true", gameNodeSource, StringComparison.Ordinal);
        Assert.Contains(
            "startInfo.ArgumentList.Add(\"--headless\")",
            gameNodeSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain("CreateNoWindow = false", gameNodeSource, StringComparison.Ordinal);
        Assert.Contains("UseShellExecute = false", relaySource, StringComparison.Ordinal);
        Assert.Contains("CreateNoWindow = true", relaySource, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void FullHarnessBuildsWindowsRuntimeOnceWithoutSharedCompilerLocks() {
        string root = FindRepositoryRoot();
        string source = File.ReadAllText(Path.Combine(
            root,
            "scripts",
            "Test-Multiplayer.ps1"));

        Assert.Matches(
            @"\$serializedBuildProperties\s*=\s*@\(\s*""-m:1""\s*,\s*""-p:UseSharedCompilation=false""\s*\)",
            source);
        Assert.Contains(
            "if (!$NoBuild -and $Mode -eq \"full\")",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "$script:coreRuntimeBuilt = $true",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "if ($NoBuild -or $script:coreRuntimeBuilt)",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void LegacyHarnessUsesSharedPortLeasesAndPerTestHangTimeout() {
        string root = FindRepositoryRoot();
        string contextSource = File.ReadAllText(Path.Combine(
            root,
            "Survivalcraft.Test",
            "Settings",
            "GameContext.cs"));
        string harnessSource = File.ReadAllText(Path.Combine(
            root,
            "scripts",
            "Test-Multiplayer.ps1"));

        Assert.Equal(
            2,
            Regex.Matches(
                contextSource,
                @"UdpPortLeaseAllocator\s*\.\s*Allocate\s*\(",
                RegexOptions.CultureInvariant).Count);
        Assert.DoesNotContain("private static class UdpPortPool", contextSource, StringComparison.Ordinal);
        Assert.Contains("\"--blame-hang\"", harnessSource, StringComparison.Ordinal);
        Assert.Contains("\"--blame-hang-timeout\", \"5m\"", harnessSource, StringComparison.Ordinal);
        Assert.Contains("\"--blame-hang-dump-type\", \"none\"", harnessSource, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void PeriodicPlayerBodySyncRemainsSelfHealingAfterReorderOrLoss() {
        string root = FindRepositoryRoot();
        string source = File.ReadAllText(Path.Combine(
            root,
            "Survivalcraft.Multiplayer",
            "NetworkModLoader.cs"));

        Assert.Contains(
            "session,\n                    forceFull: true",
            source.Replace("\r\n", "\n", StringComparison.Ordinal),
            StringComparison.Ordinal);
        Assert.Contains(
            "new PlayerBodyUpdatePacket(player, forceFull: true)",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void VitalStatsSyncCannotRegressToPerFrameBroadcasts() {
        string root = FindRepositoryRoot();
        string source = File.ReadAllText(Path.Combine(
            root,
            "Survivalcraft.Multiplayer",
            "NetworkModLoader.cs"));
        Match method = Regex.Match(
            source,
            @"public override void OnNetworkVitalStatsUpdate[\s\S]*?\n    \}",
            RegexOptions.CultureInvariant);

        Assert.True(method.Success, "Could not locate OnNetworkVitalStatsUpdate.");
        Assert.Contains(
            "Time.PeriodicEvent(0.2, 0.0)",
            method.Value,
            StringComparison.Ordinal);
        Assert.Contains(
            "NetworkStateRevisionDomain.VitalStats",
            method.Value,
            StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot() {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null) {
            if (Directory.Exists(Path.Combine(directory.FullName, "Survivalcraft.TestAutomation"))
                && Directory.Exists(Path.Combine(directory.FullName, "Survivalcraft.Testing"))) {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException(
            "Could not locate the repository root for production-path source guards.");
    }
}
