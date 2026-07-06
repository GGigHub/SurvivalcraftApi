using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Silk.NET.OpenGLES;
using Xunit;

using System.Xml.Linq;
namespace Survivalcraft.Test;

public class WorldReproducibilityTest : IClassFixture<GameFixture> {
    private readonly GameFixture fixture;
    private GameContext server => fixture.Server;
    private GameContext client => fixture.Client;

    public WorldReproducibilityTest(GameFixture fixture) {
        this.fixture = fixture;
    }

    [Fact, TestOrder(Priority = 1)]
    public async Task WorldReproducibility() {
        await GameTools.CreateTestWorld(server, "test_world");

        await server.PushCommandAsync("EXEC " + GameTools.ReadScript("GameStartup/DisconnectCode.cs"));
        bool serverMainMenu = await server.WaitLogAsync("Entered screen \"MainMenu\"");
        Assert.True(serverMainMenu, "Server failed to return to MainMenu");

        string serverWorldsPath = Path.Combine(server.RunDirectory, "doc", "Worlds");
        string clientWorldsPath = Path.Combine(client.RunDirectory, "doc", "Worlds");

        string[] worldDirs = Directory.GetDirectories(serverWorldsPath);
        Assert.NotEmpty(worldDirs);
        string serverWorldDir = worldDirs[0];
        string worldName = Path.GetFileName(serverWorldDir);
        string clientWorldDir = Path.Combine(clientWorldsPath, worldName);

        CopyDirectory(serverWorldDir, clientWorldDir);

        string startScript = GameTools.ReadScript("WorldReproducibility/StartCode.cs");

        await server.PushCommandAsync("EXEC " + startScript);
        await client.PushCommandAsync("EXEC " + startScript);

        Assert.True(await server.WaitLogAsync("Entered screen \"MainMenu\"", 120), server.GetRecentLogText());
        Assert.True(await client.WaitLogAsync("Entered screen \"MainMenu\"", 120), client.GetRecentLogText());

        bool areIdentical = CompareDirectories(serverWorldDir, clientWorldDir);
        Assert.True(areIdentical, "Worlds are not identical after running independently!");
    }

    private static void CopyDirectory(string sourceDir, string targetDir) {
        Directory.CreateDirectory(targetDir);
        foreach (var file in Directory.GetFiles(sourceDir)) {
            File.Copy(file, Path.Combine(targetDir, Path.GetFileName(file)), true);
        }
        foreach (var dir in Directory.GetDirectories(sourceDir)) {
            CopyDirectory(dir, Path.Combine(targetDir, Path.GetFileName(dir)));
        }
    }

    private static bool CompareDirectories(string dir1, string dir2) {
        var files1 = Directory.GetFiles(dir1, "*.*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(dir1, f))
            .OrderBy(f => f)
            .ToArray();
        var files2 = Directory.GetFiles(dir2, "*.*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(dir2, f))
            .OrderBy(f => f)
            .ToArray();

        var commonFiles = files1.Intersect(files2).ToArray();
        if (files1.Length != files2.Length) {
            throw new Exception($"File count mismatch. Server: {files1.Length}, Client: {files2.Length}. Server unique: {string.Join(", ", files1.Except(files2))}. Client unique: {string.Join(", ", files2.Except(files1))}");
        }

        foreach (var relative in commonFiles) {
            var path1 = Path.Combine(dir1, relative);
            var path2 = Path.Combine(dir2, relative);

            if (relative.EndsWith("Project.xml", StringComparison.OrdinalIgnoreCase)) {
                var xml1 = XDocument.Load(path1).Root;
                var xml2 = XDocument.Load(path2).Root;
                var diffs = CompareXml(xml1, xml2, "Project");
                if (diffs.Count > 0) {
                    throw new Exception($"Differences in {relative}:\n" + string.Join("\n", diffs.Take(50)) + (diffs.Count > 50 ? $"\n... and {diffs.Count - 50} more" : ""));
                }
                continue;
            }
            if (relative.EndsWith("Project.bak", StringComparison.OrdinalIgnoreCase)) continue; // ignore Project.bak

            var bytes1 = File.ReadAllBytes(path1);
            var bytes2 = File.ReadAllBytes(path2);

            if (bytes1.Length != bytes2.Length) {
                throw new Exception($"Size mismatch in file: {relative}. Server: {bytes1.Length}, Client: {bytes2.Length}");
            }

            for (int j = 0; j < bytes1.Length; j++) {
                if (bytes1[j] != bytes2[j]) {
                    throw new Exception($"Content mismatch in file: {relative} at byte {j}. Server: {bytes1[j]}, Client: {bytes2[j]}");
                }
            }
        }

        return true;
    }

    private static List<string> CompareXml(XElement? el1, XElement? el2, string path) {
        var diffs = new List<string>();

        if (el1.Name != el2.Name) {
            diffs.Add($"{path}: Name {el1.Name} != {el2.Name}");
        }

        var attrs1 = el1.Attributes().ToDictionary(a => a.Name);
        var attrs2 = el2.Attributes().ToDictionary(a => a.Name);

        foreach (var a1 in attrs1.Values) {
            if (!attrs2.TryGetValue(a1.Name, out var a2)) {
                diffs.Add($"{path}/@{a1.Name}: missing in client");
            }
            else if (a1.Value != a2.Value) {
                diffs.Add($"{path}/@{a1.Name}: '{a1.Value}' != '{a2.Value}'");
            }
        }
        foreach (var a2 in attrs2.Values) {
            if (!attrs1.ContainsKey(a2.Name)) {
                diffs.Add($"{path}/@{a2.Name}: missing in server");
            }
        }

        var children1 = el1.Elements().ToList();
        var children2 = el2.Elements().ToList();

        for (int i = 0; i < Math.Max(children1.Count, children2.Count); i++) {
            if (i >= children1.Count) {
                diffs.Add($"{path}: extra child in client: {children2[i].Name}");
            }
            else if (i >= children2.Count) {
                diffs.Add($"{path}: extra child in server: {children1[i].Name}");
            }
            else {
                string childId = children1[i].Attribute("Name")?.Value ?? children1[i].Attribute("Id")?.Value ?? i.ToString();
                diffs.AddRange(CompareXml(children1[i], children2[i], $"{path}/{children1[i].Name}[{childId}]"));
            }
        }

        return diffs;
    }

    [Fact]
    public void Dispose() {
        fixture.ThrowIfAnyContextHasError();
    }
}
