using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Silk.NET.OpenGLES;
using Xunit;

namespace Survivalcraft.Test; 
public class WorldReproducibilityTest : IClassFixture<GameFixture>{
    private readonly GameFixture fixture;
    private GameContext server => fixture.Server;
    private GameContext client => fixture.Client;

    public WorldReproducibilityTest(GameFixture fixture) {
        this.fixture = fixture;
    }

    [Fact, TestOrder(Priority = 1)]
    public async Task WorldReproducibility() { 
        await GameTools.CreateTestWorld(server);

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

        await server.PushCommandAsync("EXEC WorldsManager.UpdateWorldsList(); ScreensManager.SwitchScreen(\"GameLoading\", WorldsManager.WorldInfos[0], null);");
        await client.PushCommandAsync("EXEC WorldsManager.UpdateWorldsList(); ScreensManager.SwitchScreen(\"GameLoading\", WorldsManager.WorldInfos[0], null);");

        Assert.True(await server.WaitLogAsync("Entered screen \"Game\""));
        Assert.True(await client.WaitLogAsync("Entered screen \"Game\""));

        await Task.Delay(10000);

        await server.PushCommandAsync("EXEC " + GameTools.ReadScript("GameStartup/DisconnectCode.cs"));
        await client.PushCommandAsync("EXEC " + GameTools.ReadScript("GameStartup/DisconnectCode.cs"));

        Assert.True(await server.WaitLogAsync("Entered screen \"MainMenu\""));
        Assert.True(await client.WaitLogAsync("Entered screen \"MainMenu\""));

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
            if (relative.EndsWith("Project.xml", StringComparison.OrdinalIgnoreCase)) continue; // ignore Project.xml
            if (relative.EndsWith("Project.bak", StringComparison.OrdinalIgnoreCase)) continue; // ignore Project.bak

            var path1 = Path.Combine(dir1, relative);
            var path2 = Path.Combine(dir2, relative);

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

    [Fact]
    public void Dispose() {
        fixture.ThrowIfAnyContextHasError();
    }
}
