using System;
using System.Globalization;
using System.Threading.Tasks;
using Xunit;

namespace Survivalcraft.Test;

public static class HealthSyncTools {
    public const float HealthEpsilon = 0.001f;
    public static async Task ConnectTestServerAsync(GameContext client, int serverPort) {
        await client.PushCommandAsync(
            "EXEC " + GameTools.ReadScript("GameStartup/SwitchToGameLoadingCode.cs").Replace("{0}", serverPort.ToString()));

        bool connected = await client.WaitLogAsync($"Connected: 127.0.0.1:{serverPort}");
        Assert.True(connected, "Client failed to connect to the server.");

        bool playerScreen = await client.WaitLogAsync("Entered screen \"Player\"");
        Assert.True(playerScreen, "Failed to load the Player screen.");

        await client.PushCommandAsync("CLICK_WIDGET PlayButton");
        int playerIndex = await WaitForClientPlayerIndexAsync(client);
        Assert.True(playerIndex >= 0, "Client player index was not assigned.");
    }

    public static async Task<int> SpawnHealthTestCreatureAsync(GameContext context) {
        object? result = await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("HealthSync/SpawnHealthTestCreatureCode.cs"));

        return result is int id ? id : Convert.ToInt32(result);
    }

    public static async Task<bool> WaitForEntityExistsAsync(GameContext context, int entityId) {
        WaitResult<bool> result = await TestWaiter.TryUntilAsync(
            async () => {
                object? value = await context.PushCommandAsync(
                    $"FUNC return Game.GameManager.Project.FindEntity({entityId}) != null;");
                return value is bool exists && exists;
            },
            exists => exists,
            TestTimeouts.DefaultEventually);
        return result.Matched;
    }

    public static async Task<double> InjureEntityAsync(GameContext context, int entityId, float amount, string cause) {
        object? result = await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("HealthSync/InjureEntityCode.cs")
                .Replace("{entityId}", entityId.ToString(CultureInfo.InvariantCulture))
                .Replace("{amount:R}", amount.ToString("R", CultureInfo.InvariantCulture))
                .Replace("{cause}", cause));

        return Convert.ToDouble(result);
    }

    public static async Task<double> InjureExistingEntityAsync(GameContext context, int entityId, float amount, string cause) {
        object? result = await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("HealthSync/InjureExistingEntityCode.cs")
                .Replace("{entityId}", entityId.ToString(CultureInfo.InvariantCulture))
                .Replace("{amount:R}", amount.ToString("R", CultureInfo.InvariantCulture))
                .Replace("{cause}", cause));

        return Convert.ToDouble(result);
    }

    public static async Task<double> SetEntityHealthAsync(GameContext context, int entityId, float value) {
        object? result = await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("HealthSync/SetEntityHealthCode.cs")
                .Replace("{entityId}", entityId.ToString(CultureInfo.InvariantCulture))
                .Replace("{value:R}", value.ToString("R", CultureInfo.InvariantCulture)));

        return Convert.ToDouble(result);
    }

    public static async Task<double> WaitForHealthToExceedAsync(GameContext context, int entityId, double expectedMinHealth) {
        return await TestWaiter.UntilAsync(
            () => GetHealthAsync(context, entityId),
            health => health >= expectedMinHealth,
            TestTimeouts.DefaultEventually,
            $"Health did not exceed {expectedMinHealth} for entity {entityId}.");
    }

    public static async Task EnableEntityNaturalHealingAsync(GameContext context, int entityId) {
        await context.PushCommandAsync(
            "EXEC " + GameTools.ReadScript("HealthSync/EnableEntityNaturalHealingCode.cs").Replace("{0}", entityId.ToString(CultureInfo.InvariantCulture)));
    }

    public static async Task<double> WaitForHealthAsync(GameContext context, int entityId, double expectedHealth) {
        return await TestWaiter.UntilAsync(
            () => GetHealthAsync(context, entityId),
            health => Math.Abs(health - expectedHealth) <= HealthEpsilon,
            TestTimeouts.DefaultEventually,
            $"Health did not reach {expectedHealth} for entity {entityId}.");
    }

    public static async Task<double> WaitForHealthAtMostAsync(GameContext context, int entityId, double expectedMaxHealth) {
        return await TestWaiter.UntilAsync(
            () => GetHealthAsync(context, entityId),
            health => health <= expectedMaxHealth + HealthEpsilon,
            TestTimeouts.DefaultEventually,
            $"Health did not fall to {expectedMaxHealth} or lower for entity {entityId}.");
    }

    public static async Task<double> GetHealthAsync(GameContext context, int entityId) {
        object? result = await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("HealthSync/GetHealthCode.cs")
                .Replace("{entityId}", entityId.ToString(CultureInfo.InvariantCulture)));

        return Convert.ToDouble(result);
    }

    public static async Task<string> WaitForCauseOfDeathAsync(GameContext context, int entityId, string expectedCause) {
        return await TestWaiter.UntilAsync(
            () => GetCauseOfDeathAsync(context, entityId),
            cause => cause == expectedCause,
            TestTimeouts.DefaultEventually,
            $"CauseOfDeath did not reach \"{expectedCause}\" for entity {entityId}.");
    }

    public static async Task<string> GetCauseOfDeathAsync(GameContext context, int entityId) {
        object? result = await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("HealthSync/GetCauseOfDeathCode.cs")
                .Replace("{entityId}", entityId.ToString(CultureInfo.InvariantCulture)));

        return result as string ?? string.Empty;
    }

    public static async Task<int> WaitForClientPlayerIndexAsync(GameContext context) {
        return await TestWaiter.UntilAsync(
            async () => {
                object? result = await context.PushCommandAsync("FUNC return Game.NetworkManager.PlayerIndex;");
                return result is int id ? id : Convert.ToInt32(result);
            },
            playerIndex => playerIndex >= 0,
            TestTimeouts.DefaultEventually,
            "Client player index was not assigned.");
    }

    public static async Task<int> EnsureServerPlayerEntityAsync(GameContext context, int playerIndex) {
        object? result = await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("HealthSync/EnsureServerPlayerEntityCode.cs").Replace("{0}", playerIndex.ToString(CultureInfo.InvariantCulture)));

        return result is int id ? id : Convert.ToInt32(result);
    }

    public static async Task<double> SetPlayerHealthAsync(GameContext context, int playerIndex, float value) {
        object? result = await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("HealthSync/SetPlayerHealthCode.cs").Replace("{0}", playerIndex.ToString(CultureInfo.InvariantCulture)).Replace("{1}", value.ToString(CultureInfo.InvariantCulture)));

        return Convert.ToDouble(result);
    }

    public static async Task<double> WaitForPlayerHealthAsync(GameContext context, int playerIndex, double expectedHealth) {
        return await TestWaiter.UntilAsync(
            () => GetPlayerHealthAsync(context, playerIndex),
            health => Math.Abs(health - expectedHealth) <= HealthEpsilon,
            TestTimeouts.DefaultEventually,
            $"Player {playerIndex} health did not reach {expectedHealth}.");
    }

    public static async Task<double> GetPlayerHealthAsync(GameContext context, int playerIndex) {
        object? result = await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("HealthSync/GetPlayerHealthCode.cs").Replace("{0}", playerIndex.ToString(CultureInfo.InvariantCulture)));

        return Convert.ToDouble(result);
    }

    public static async Task SimulateClientSuicideButtonClickAsync(GameContext context) {
        await ClickWidgetAndWaitAsync(context, "ClothingButton", IsClothingWidgetVisibleAsync);
        await ClickWidgetAndWaitAsync(context, "VitalStatsButton", IsVitalStatsWidgetVisibleAsync);
        await ClickWidgetAndWaitAsync(context, "ChokeButton", IsVitalStatsWidgetVisibleAsync);
    }

    public static async Task SimulateForgedClientFallAsync(GameContext client, int playerIndex) {
        // Trigger Injure on the client side with a Fall cause.
        // This will naturally broadcast an Injure packet to the server as if the client just fell.
        // The server will evaluate this packet and ignore it because the server proxy is standing.
        await client.PushCommandAsync(
            "EXEC " + GameTools.ReadScript("HealthSync/SimulateForgedClientFallCode.cs").Replace("{0}", playerIndex.ToString(CultureInfo.InvariantCulture)));
    }

    public static async Task SetPlayerVitalStatsAsync(GameContext context, int playerIndex, float food, float stamina, float sleep, float wetness, float temperature) {
        await context.PushCommandAsync(
            "EXEC " + GameTools.ReadScript("HealthSync/SetPlayerVitalStatsCode.cs").Replace("{0}", playerIndex.ToString(CultureInfo.InvariantCulture)).Replace("{1}", food.ToString(CultureInfo.InvariantCulture)).Replace("{2}", stamina.ToString(CultureInfo.InvariantCulture)).Replace("{3}", sleep.ToString(CultureInfo.InvariantCulture)).Replace("{4}", wetness.ToString(CultureInfo.InvariantCulture)).Replace("{5}", temperature.ToString(CultureInfo.InvariantCulture)));
    }

    public static async Task<float[]> GetPlayerVitalStatsAsync(GameContext context, int playerIndex) {
        object? result = await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("HealthSync/GetPlayerVitalStatsCode.cs").Replace("{0}", playerIndex.ToString(CultureInfo.InvariantCulture)));

        string res = result as string ?? "";
        if (string.IsNullOrEmpty(res)) return Array.Empty<float>();
        string[] parts = res.Split(',');
        return new float[] {
            float.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
            float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture),
            float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture),
            float.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture),
            float.Parse(parts[4], System.Globalization.CultureInfo.InvariantCulture)
        };
    }

    public static async Task WaitForPlayerVitalStatsAsync(GameContext context, int playerIndex, float food, float stamina, float sleep, float wetness, float temperature) {
        await TestWaiter.UntilAsync(
            () => GetPlayerVitalStatsAsync(context, playerIndex),
            stats => {
                if (stats.Length == 5) {
                // Only strictly verify food and sleep because they change very slowly.
                // Stamina, wetness, and temperature simulate too quickly towards their target values to assert exact limits after a network delay.
                    if (Math.Abs(stats[0] - food) <= 0.1f &&
                        Math.Abs(stats[2] - sleep) <= 0.1f) {
                        return true;
                    }
                }
                return false;
            },
            TestTimeouts.DefaultEventually,
            $"Player {playerIndex} vital stats did not reach {food:R}, {stamina:R}, {sleep:R}, {wetness:R}, {temperature:R}.");
    }

    public static async Task ClickWidgetAndWaitAsync(GameContext context, string widgetName, Func<GameContext, Task<bool>> waitUntil) {
        for (int i = 0; i < 20; i++) {
            await context.PushCommandAsync($"CLICK_WIDGET {widgetName}");
            WaitResult<bool> result = await TestWaiter.TryUntilAsync(
                () => waitUntil(context),
                matched => matched,
                TestTimeouts.DefaultEventually);
            if (result.Matched) {
                return;
            }
        }

        throw new TimeoutException($"Widget click did not reach expected state: {widgetName}.");
    }

    public static async Task<bool> IsClothingWidgetVisibleAsync(GameContext context) {
        object? result = await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("HealthSync/IsClothingWidgetVisibleCode.cs"));

        return result is bool visible && visible;
    }

    public static async Task<bool> IsVitalStatsWidgetVisibleAsync(GameContext context) {
        object? result = await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("HealthSync/IsVitalStatsWidgetVisibleCode.cs"));

        return result is bool visible && visible;
    }

    public static string FormatVitalStats(float[] stats) {
        return stats.Length == 5
            ? $"{stats[0]:R}, {stats[1]:R}, {stats[2]:R}, {stats[3]:R}, {stats[4]:R}"
            : "none";
    }
}
