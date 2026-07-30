using System;
using System.Threading.Tasks;
using Xunit;

namespace Survivalcraft.Test;

[TestCaseOrderer("Survivalcraft.Test.PriorityOrderer", "Survivalcraft.Test")]
public sealed class EnvironmentSyncTest : IClassFixture<GameFixture> {
    private readonly GameFixture fixture;
    private GameContext server => fixture.Server;
    private GameContext client => fixture.Client;
    private GameContext spectator => fixture.Spectator;

    public EnvironmentSyncTest(GameFixture fixture) {
        this.fixture = fixture;
    }

    [Fact, TestOrder(Priority = 1)]
    public async Task TestWorldCreationAndConnection() {
        await EnvironmentSyncTools.CreateCreativeTestWorldAsync(server, "world-environment-sync");
        await EnvironmentSyncTools.ConnectTestServerAsync(client, server.GamePort);
        Assert.True(await EnvironmentSyncTools.WaitForMainPlayerAsync(client), "Client main player was not ready.");
        await EnsureSpectatorConnectedAsync();
    }

    [Fact, TestOrder(Priority = 2)]
    public async Task TestWorldTimeSyncsFromServer() {
        EnvironmentSyncTools.WorldEnvironmentState initial = await EnvironmentSyncTools.GetStateAsync(server);
        EnvironmentSyncTools.WorldEnvironmentState serverAdvanced =
            await EnvironmentSyncTools.WaitForServerTimeAtLeastAsync(server, initial.TotalElapsedGameTime + 2d);
        EnvironmentSyncTools.WorldEnvironmentState clientSynced =
            await EnvironmentSyncTools.WaitForTimeCloseAsync(client, serverAdvanced);

        Assert.True(
            Math.Abs(clientSynced.TotalElapsedGameTime - serverAdvanced.TotalElapsedGameTime) <= EnvironmentSyncTools.TimeTolerance,
            $"World time did not synchronize. Server={serverAdvanced.Raw}, Client={clientSynced.Raw}");
        Assert.Equal(serverAdvanced.TimeOfDayOffset, clientSynced.TimeOfDayOffset, 3);
    }

    [Fact, TestOrder(Priority = 3)]
    public async Task TestPrecipitationScheduleSyncsFromServerWeather() {
        await EnvironmentSyncTools.ForcePrecipitationRescheduleAsync(server);
        EnvironmentSyncTools.WorldEnvironmentState serverWeather =
            await EnvironmentSyncTools.WaitForPrecipitationScheduleAsync(server);
        EnvironmentSyncTools.WorldEnvironmentState clientWeather =
            await EnvironmentSyncTools.WaitForPrecipitationScheduleAsync(client, serverWeather);

        Assert.True(
            EnvironmentSyncTools.PrecipitationScheduleMatches(serverWeather, clientWeather),
            $"Precipitation schedule did not synchronize. Server={serverWeather.Raw}, Client={clientWeather.Raw}");
    }

    [Fact, TestOrder(Priority = 4)]
    public async Task TestFogScheduleSyncsFromServerWeather() {
        await EnvironmentSyncTools.ForceFogRescheduleAsync(server);
        EnvironmentSyncTools.WorldEnvironmentState serverFog =
            await EnvironmentSyncTools.WaitForFogScheduleAsync(server);
        EnvironmentSyncTools.WorldEnvironmentState clientFog =
            await EnvironmentSyncTools.WaitForFogScheduleAsync(client, serverFog);

        Assert.True(
            EnvironmentSyncTools.FogScheduleMatches(serverFog, clientFog),
            $"Fog schedule did not synchronize. Server={serverFog.Raw}, Client={clientFog.Raw}");
    }

    [Fact, TestOrder(Priority = 5)]
    public async Task TestClientManualTimeChangeSyncsToServerAndObserver() {
        await EnsureSpectatorConnectedAsync();

        EnvironmentSyncTools.WorldEnvironmentState initialClient = await EnvironmentSyncTools.GetStateAsync(client);
        await EnvironmentSyncTools.TriggerManualEnvironmentInputAsync(client, EnvironmentSyncTools.ManualEnvironmentInput.TimeOfDay);
        EnvironmentSyncTools.WorldEnvironmentState changedClient = await EnvironmentSyncTools.WaitForStateChangeAsync(
            client,
            state => Math.Abs(state.TimeOfDayOffset - initialClient.TimeOfDayOffset) > EnvironmentSyncTools.DoubleTolerance,
            "Client time-of-day offset did not change after manual input.");

        EnvironmentSyncTools.WorldEnvironmentState serverTime =
            await EnvironmentSyncTools.WaitForTimeOfDayOffsetAsync(server, changedClient.TimeOfDayOffset);
        EnvironmentSyncTools.WorldEnvironmentState spectatorTime =
            await EnvironmentSyncTools.WaitForTimeOfDayOffsetAsync(spectator, changedClient.TimeOfDayOffset);

        Assert.Equal(changedClient.TimeOfDayOffset, serverTime.TimeOfDayOffset, 3);
        Assert.Equal(changedClient.TimeOfDayOffset, spectatorTime.TimeOfDayOffset, 3);
    }

    [Fact, TestOrder(Priority = 6)]
    public async Task TestClientManualLightningSyncsToServerAndObserver() {
        await EnsureSpectatorConnectedAsync();

        EnvironmentSyncTools.WorldEnvironmentState initialServer = await EnvironmentSyncTools.GetStateAsync(server);
        EnvironmentSyncTools.WorldEnvironmentState initialSpectator = await EnvironmentSyncTools.GetStateAsync(spectator);
        await EnvironmentSyncTools.TriggerManualEnvironmentInputAsync(client, EnvironmentSyncTools.ManualEnvironmentInput.Lightning);

        EnvironmentSyncTools.WorldEnvironmentState serverLightning =
            await EnvironmentSyncTools.WaitForLightningAfterAsync(server, initialServer.LastLightningStrikeTime);
        EnvironmentSyncTools.WorldEnvironmentState spectatorLightning =
            await EnvironmentSyncTools.WaitForLightningAfterAsync(spectator, initialSpectator.LastLightningStrikeTime);

        Assert.True(serverLightning.LastLightningStrikeTime > initialServer.LastLightningStrikeTime, $"Server did not receive manual lightning. Server={serverLightning.Raw}");
        Assert.True(spectatorLightning.LastLightningStrikeTime > initialSpectator.LastLightningStrikeTime, $"Spectator did not receive manual lightning. Spectator={spectatorLightning.Raw}");
    }

    [Fact, TestOrder(Priority = 7)]
    public async Task TestClientManualPrecipitationSyncsToServerAndObserver() {
        await EnsureSpectatorConnectedAsync();

        await EnvironmentSyncTools.TriggerManualEnvironmentInputAsync(client, EnvironmentSyncTools.ManualEnvironmentInput.Precipitation);
        EnvironmentSyncTools.WorldEnvironmentState serverWeather =
            await EnvironmentSyncTools.WaitForPrecipitationStartedAsync(server, expected: true);
        EnvironmentSyncTools.WorldEnvironmentState spectatorWeather =
            await EnvironmentSyncTools.WaitForPrecipitationStartedAsync(spectator, expected: true);

        Assert.True(serverWeather.IsPrecipitationStarted, $"Server did not receive manual precipitation. Server={serverWeather.Raw}");
        Assert.True(spectatorWeather.IsPrecipitationStarted, $"Spectator did not receive manual precipitation. Spectator={spectatorWeather.Raw}");
    }

    [Fact, TestOrder(Priority = 8)]
    public async Task TestClientManualFogSyncsToServerAndObserver() {
        await EnsureSpectatorConnectedAsync();

        await EnvironmentSyncTools.TriggerManualEnvironmentInputAsync(client, EnvironmentSyncTools.ManualEnvironmentInput.Fog);
        EnvironmentSyncTools.WorldEnvironmentState serverFog =
            await EnvironmentSyncTools.WaitForFogStartedAsync(server, expected: true);
        EnvironmentSyncTools.WorldEnvironmentState spectatorFog =
            await EnvironmentSyncTools.WaitForFogStartedAsync(spectator, expected: true);

        Assert.True(serverFog.IsFogStarted, $"Server did not receive manual fog. Server={serverFog.Raw}");
        Assert.True(spectatorFog.IsFogStarted, $"Spectator did not receive manual fog. Spectator={spectatorFog.Raw}");
    }

    [Fact]
    public void Dispose() {
        fixture.ThrowIfAnyContextHasError();
    }

    private async Task EnsureSpectatorConnectedAsync() {
        if (!spectator.IsStarted) {
            fixture.StartSpectator();
            await EnvironmentSyncTools.ConnectTestServerAsync(spectator, server.GamePort);
            Assert.True(await EnvironmentSyncTools.WaitForMainPlayerAsync(spectator), "Spectator main player was not ready.");
        }
    }
}
