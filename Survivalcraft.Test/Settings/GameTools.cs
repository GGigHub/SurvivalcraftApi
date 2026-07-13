using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace Survivalcraft.Test; 

public static class GameTools {
    public static async Task CreateTestWorld(GameContext server, string seed = "") {
        await server.PushCommandAsync("CLICK_WIDGET Play");
        bool playScreenEntered = await server.WaitLogAsync("Entered screen \"Play\"");
        Assert.True(playScreenEntered, "Failed to enter the Play screen");

        await server.PushCommandAsync("CLICK_WIDGET NewWorld");
        bool newWorldScreenEntered = await server.WaitLogAsync("Entered screen \"NewWorld\"");
        Assert.True(newWorldScreenEntered, "Failed to enter the New World screen");

        if (!string.IsNullOrEmpty(seed)) {
            await TestWaiter.UntilAsync(
                async () => await server.PushCommandAsync("FUNC return (ScreensManager.CurrentScreen as NewWorldScreen)?.m_worldSettings != null;") is bool ready && ready,
                ready => ready,
                TestTimeouts.DefaultEventually,
                "NewWorldScreen world settings were not ready.");
            await server.PushCommandAsync($"EXEC (ScreensManager.CurrentScreen as NewWorldScreen).m_seedTextBox.Text = \"{seed}\";");
        }
        await server.PushCommandAsync("CLICK_WIDGET Play");
        bool playerScreen = await server.WaitLogAsync("Entered screen \"Player\"");
        Assert.True(playerScreen, "Failed to load the Player screen");

        await server.PushCommandAsync("CLICK_WIDGET PlayButton");
        bool gameEntered = await server.WaitLogAsync("Entered screen \"Game\"");
        Assert.True(gameEntered, "Failed to enter the Game screen");
    }

    public static async Task ConnectTestServer(GameContext client, int serverPort) {
        await client.PushCommandAsync("EXEC " + ReadScript("GameStartup/SwitchToGameLoadingCode.cs").Replace("{0}", serverPort.ToString()));

        bool connected = await client.WaitLogAsync($"Connected: 127.0.0.1:{serverPort}");
        Assert.True(connected, "Client failed to connect to the server.");

        bool playerScreen = await client.WaitLogAsync("Entered screen \"Player\"");
        Assert.True(playerScreen, "Failed to load the Player screen");

        await client.PushCommandAsync("CLICK_WIDGET PlayButton");
        bool intoPlaying = await client.WaitLogAsync("Player into playing.");
        Assert.True(intoPlaying, "Failed into playing state");
    }

    public static string ReadScript(string path) {
        return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scripts", path));
    }
}
