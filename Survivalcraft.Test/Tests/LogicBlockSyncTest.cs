using System;
using System.Threading.Tasks;
using Xunit;

namespace Survivalcraft.Test;

[TestCaseOrderer("Survivalcraft.Test.PriorityOrderer", "Survivalcraft.Test")]
public sealed class LogicBlockSyncTest : IClassFixture<GameFixture> {
    private readonly GameFixture fixture;
    private GameContext server => fixture.Server;
    private GameContext client => fixture.Client;
    private GameContext spectator => fixture.Spectator;

    public LogicBlockSyncTest(GameFixture fixture) {
        this.fixture = fixture;
    }

    [Fact, TestOrder(Priority = 1)]
    public async Task TestEditableLogicBlockSettingsSync() {
        await GameTools.CreateTestWorld(server, "logic-block-sync");
        await LogicBlockSyncTools.ConnectTestServerAsync(client, server.GamePort);
        Assert.True(await LogicBlockSyncTools.WaitForMainPlayerAsync(client), "Client main player was not ready.");

        foreach (LogicBlockSyncTools.LogicBlockCase testCase in LogicBlockSyncTools.EditableSettingCases) {
            LogicBlockSyncTools.LogicBlockState target = await LogicBlockSyncTools.CreateLogicBlockAsync(server, testCase);
            Assert.Equal(testCase.BlockIndex, target.Contents);
            Assert.Equal(testCase.InitialData, target.Data);

            LogicBlockSyncTools.LogicBlockState clientInitial = await LogicBlockSyncTools.WaitForLogicBlockStateAsync(
                client,
                target,
                testCase.BlockIndex,
                testCase.InitialData);
            Assert.Equal(testCase.InitialData, clientInitial.Data);

            string editResult = await LogicBlockSyncTools.EditLogicBlockAsync(client, target, testCase);
            Assert.Equal("edited", editResult);

            LogicBlockSyncTools.LogicBlockState serverEdited = await LogicBlockSyncTools.WaitForLogicBlockStateAsync(
                server,
                target,
                testCase.BlockIndex,
                testCase.ExpectedData);
            LogicBlockSyncTools.LogicBlockState clientEdited = await LogicBlockSyncTools.WaitForLogicBlockStateAsync(
                client,
                target,
                testCase.BlockIndex,
                testCase.ExpectedData);

            Assert.True(
                serverEdited.Data == testCase.ExpectedData && clientEdited.Data == testCase.ExpectedData,
                $"{testCase.Kind} data did not sync. Server={serverEdited.Raw}, Client={clientEdited.Raw}, ExpectedData={testCase.ExpectedData}.");
        }
    }

    [Fact, TestOrder(Priority = 2)]
    public async Task TestStructuredLogicBlockDataSync() {
        foreach (LogicBlockSyncTools.StructuredBlockCase testCase in LogicBlockSyncTools.StructuredDataCases) {
            LogicBlockSyncTools.LogicBlockState target = await LogicBlockSyncTools.CreateStructuredBlockAsync(server, testCase);
            Assert.Equal(testCase.BlockIndex, target.Contents);
            Assert.Equal(testCase.InitialData, target.Data);

            LogicBlockSyncTools.LogicBlockState clientInitial = await LogicBlockSyncTools.WaitForLogicBlockStateAsync(
                client,
                target,
                testCase.BlockIndex,
                testCase.InitialData);
            Assert.Equal(testCase.InitialData, clientInitial.Data);

            string editResult = await LogicBlockSyncTools.EditStructuredBlockDataAsync(client, target, testCase);
            Assert.Equal("edited", editResult);

            LogicBlockSyncTools.StructuredBlockState serverEdited = await LogicBlockSyncTools.WaitForStructuredBlockStateAsync(
                server,
                target,
                testCase);
            LogicBlockSyncTools.StructuredBlockState clientEdited = await LogicBlockSyncTools.WaitForStructuredBlockStateAsync(
                client,
                target,
                testCase);

            Assert.True(
                LogicBlockSyncTools.StructuredBlockStatesMatch(serverEdited, testCase)
                && LogicBlockSyncTools.StructuredBlockStatesMatch(clientEdited, testCase),
                $"{testCase.Kind} data did not sync. Server={serverEdited.Raw}, Client={clientEdited.Raw}.");
        }
    }

    [Fact, TestOrder(Priority = 3)]
    public async Task TestPistonMovingBlockAndDispenserSettingsSync() {
        await VerifyPistonMovingBlockSyncAsync();
        await VerifyDispenserSettingsSyncAsync();
    }

    private async Task VerifyPistonMovingBlockSyncAsync() {
        LogicBlockSyncTools.MechanicalBlockCase testCase = LogicBlockSyncTools.PistonMotionCase;
        LogicBlockSyncTools.LogicBlockState target = await LogicBlockSyncTools.CreateMechanicalBlockAsync(server, testCase);
        Assert.Equal(testCase.BlockIndex, target.Contents);
        Assert.Equal(testCase.InitialData, target.Data);

        LogicBlockSyncTools.LogicBlockState clientInitial = await LogicBlockSyncTools.WaitForLogicBlockStateAsync(
            client,
            target,
            testCase.BlockIndex,
            testCase.InitialData);
        Assert.Equal(testCase.InitialData, clientInitial.Data);

        LogicBlockSyncTools.PistonPushBlockState pushedBlock = await LogicBlockSyncTools.PlacePistonPushBlockAsync(server, target);
        Assert.True(pushedBlock.Value != 0, $"Failed to place piston push block. State={pushedBlock.Raw}");

        string moveResult = await LogicBlockSyncTools.TriggerPistonMotionAsync(server, target, 8);
        Assert.Equal("moving", moveResult);

        LogicBlockSyncTools.MechanicalBlockState serverMoving = await LogicBlockSyncTools.WaitForPistonMovingAsync(server, target);
        LogicBlockSyncTools.MechanicalBlockState clientMoving = await LogicBlockSyncTools.WaitForPistonMovingAsync(client, target);
        Assert.True(
            serverMoving.PistonMoving > 0 && clientMoving.PistonMoving > 0,
            $"Piston moving block set was not synchronized. Server={serverMoving.Raw}, Client={clientMoving.Raw}.");
        Assert.Contains(pushedBlock.Signature, serverMoving.PistonBlocks, StringComparison.Ordinal);
        Assert.Contains(pushedBlock.Signature, clientMoving.PistonBlocks, StringComparison.Ordinal);
        Assert.Equal(serverMoving.PistonBlocks, clientMoving.PistonBlocks);

        string stopResult = await LogicBlockSyncTools.StopPistonMotionAsync(server, target);
        Assert.Equal("stopped", stopResult);

        LogicBlockSyncTools.MechanicalBlockState clientStopped = await LogicBlockSyncTools.WaitForNoPistonMovingAsync(client, target);
        Assert.Equal(0, clientStopped.PistonMoving);
    }

    private async Task VerifyDispenserSettingsSyncAsync() {
        LogicBlockSyncTools.MechanicalBlockCase testCase = LogicBlockSyncTools.DispenserSettingsCase;
        LogicBlockSyncTools.LogicBlockState target = await LogicBlockSyncTools.CreateMechanicalBlockAsync(server, testCase);
        Assert.Equal(testCase.BlockIndex, target.Contents);
        Assert.Equal(testCase.InitialData, target.Data);

        LogicBlockSyncTools.MechanicalBlockState clientInitial = await LogicBlockSyncTools.WaitForDispenserStateAsync(
            client,
            target,
            testCase.InitialData);
        Assert.True(clientInitial.DispenserEntity, $"Client did not create dispenser entity. State={clientInitial.Raw}");

        string editResult = await LogicBlockSyncTools.EditDispenserSettingsAsync(client, target);
        Assert.Equal("edited", editResult);

        const int expectedData = 24;
        LogicBlockSyncTools.MechanicalBlockState serverEdited = await LogicBlockSyncTools.WaitForDispenserStateAsync(
            server,
            target,
            expectedData);
        LogicBlockSyncTools.MechanicalBlockState clientEdited = await LogicBlockSyncTools.WaitForDispenserStateAsync(
            client,
            target,
            expectedData);

        Assert.True(
            serverEdited.Data == expectedData && clientEdited.Data == expectedData,
            $"Dispenser settings were not synchronized. Server={serverEdited.Raw}, Client={clientEdited.Raw}, ExpectedData={expectedData}.");
    }

    // 首先判断 Counter 电压从 1/15 到 2/15
    // 然后判断 spectator 是否能在中途加入并获取到当前电压
    // 最后判断 Counter 电压被移除后，客户端是否能同步到移除状态
    // 第一次上电，电压为 1/15，断电并且第二次上电，电压为 2/15
    [Fact, TestOrder(Priority = 4)]
    public async Task TestCounterPersistentVoltageSyncsAfterInputChanges() {
        LogicBlockSyncTools.VoltageState target = await LogicBlockSyncTools.CreateCounterVoltageCircuitAsync(server);
        Assert.Equal(LogicBlockSyncTools.CounterBlockIndex, target.Contents);

        LogicBlockSyncTools.VoltageState clientInitial = await LogicBlockSyncTools.WaitForVoltageStateAsync(client, target);
        Assert.Equal(LogicBlockSyncTools.CounterBlockIndex, clientInitial.Contents);
        Assert.Equal(0, clientInitial.Counter);
        Assert.Equal(0, clientInitial.SourceContents);

        string firstPulse = await LogicBlockSyncTools.SetCounterInputAsync(server, target, powered: true);
        Assert.Equal("powered", firstPulse);

        LogicBlockSyncTools.VoltageState serverOne = await LogicBlockSyncTools.WaitForCounterVoltageAsync(server, target, expectedCounter: 1);
        LogicBlockSyncTools.VoltageState clientOne = await LogicBlockSyncTools.WaitForPersistentVoltageAsync(client, target, expectedCounter: 1);
        AssertVoltageSynced(serverOne, clientOne, expectedCounter: 1);

        string clearInput = await LogicBlockSyncTools.SetCounterInputAsync(server, target, powered: false);
        Assert.Equal("cleared", clearInput);

        LogicBlockSyncTools.VoltageState serverReady = await LogicBlockSyncTools.WaitForCounterReadyForPulseAsync(server, target, expectedCounter: 1);
        Assert.True(serverReady.PlusAllowed, $"Counter was not ready for the next pulse. State={serverReady.Raw}");

        string secondPulse = await LogicBlockSyncTools.SetCounterInputAsync(server, target, powered: true);
        Assert.Equal("powered", secondPulse);

        LogicBlockSyncTools.VoltageState serverTwo = await LogicBlockSyncTools.WaitForCounterVoltageAsync(server, target, expectedCounter: 2);
        LogicBlockSyncTools.VoltageState clientTwo = await LogicBlockSyncTools.WaitForPersistentVoltageAsync(client, target, expectedCounter: 2);
        AssertVoltageSynced(serverTwo, clientTwo, expectedCounter: 2);

        await EnsureSpectatorConnectedAsync();
        LogicBlockSyncTools.VoltageState spectatorTwo = await LogicBlockSyncTools.WaitForPersistentVoltageAsync(
            spectator,
            target,
            expectedCounter: 2);
        Assert.True(
            spectatorTwo.HasPersistent && Math.Abs(spectatorTwo.PersistentVoltage - 2f / 15f) <= 0.001f,
            $"Late-joining spectator did not receive the current persistent voltage. State={spectatorTwo.Raw}");

        string removeResult = await LogicBlockSyncTools.RemoveCounterVoltageCircuitAsync(server, target);
        Assert.Equal("removed", removeResult);

        LogicBlockSyncTools.VoltageState serverRemoved = await LogicBlockSyncTools.WaitForVoltageRemovedAsync(server, target);
        LogicBlockSyncTools.VoltageState clientRemoved = await LogicBlockSyncTools.WaitForVoltageRemovedAsync(client, target);
        AssertVoltageRemoved(serverRemoved, clientRemoved);
    }

    private static void AssertVoltageSynced(
        LogicBlockSyncTools.VoltageState serverState,
        LogicBlockSyncTools.VoltageState clientState,
        int expectedCounter) {
        float expectedVoltage = expectedCounter / 15f;
        Assert.True(
            serverState.Counter == expectedCounter
            && serverState.HasPersistent
            && Math.Abs(serverState.PersistentVoltage - expectedVoltage) <= 0.001f,
            $"Server counter voltage did not reach {expectedVoltage}. State={serverState.Raw}");
        Assert.True(
            clientState.HasPersistent
            && Math.Abs(clientState.PersistentVoltage - expectedVoltage) <= 0.001f,
            $"Client persistent voltage did not sync to {expectedVoltage}. Server={serverState.Raw}, Client={clientState.Raw}");
    }

    private static void AssertVoltageRemoved(
        LogicBlockSyncTools.VoltageState serverState,
        LogicBlockSyncTools.VoltageState clientState) {
        Assert.True(
            serverState.Contents != LogicBlockSyncTools.CounterBlockIndex && !serverState.HasPersistent,
            $"Server counter voltage was not removed. State={serverState.Raw}");
        Assert.True(
            clientState.Contents != LogicBlockSyncTools.CounterBlockIndex && !clientState.HasPersistent,
            $"Client counter voltage did not clear after removal. Server={serverState.Raw}, Client={clientState.Raw}");
    }

    private async Task EnsureSpectatorConnectedAsync() {
        if (!spectator.IsStarted) {
            spectator.Start();
            bool mainMenu = await spectator.WaitLogAsync("Entered screen \"MainMenu\"", 60);
            Assert.True(mainMenu, $"Spectator did not enter MainMenu. Recent logs:{Environment.NewLine}{spectator.GetRecentLogText()}");
        }

        await LogicBlockSyncTools.ConnectTestServerAsync(spectator, server.GamePort);
        Assert.True(await LogicBlockSyncTools.WaitForMainPlayerAsync(spectator), "Spectator main player was not ready.");
    }

    [Fact]
    public void Dispose() {
        fixture.ThrowIfAnyContextHasError();
    }
}
