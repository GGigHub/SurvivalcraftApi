using System.Text.Json;
using Survivalcraft.Testing.Control;
using Survivalcraft.Testing.Scenarios;
using Survivalcraft.Testing.Snapshots;
using Xunit;

namespace Survivalcraft.Testing.Tests;

public sealed class LogicProtocolTests {
    [Fact]
    [Trait("Layer", "L0")]
    public void AutoTestV7LogicCommandsPreserveAuthorityAndLifecycleFields() {
        TerrainCellCoordinate cell = new(12, 70, 14);
        LogicEditCommand edit = new(
            3,
            LogicElementKind.MemoryBank,
            cell,
            StructuredData: "1234ABCD",
            LastOutput: 9,
            ForgeAuthoritativeState: true,
            ExpectRejected: true,
            DuplicateCount: 3,
            ExpectedRevision: 7,
            TerrainRevision: 11);
        RunMovingBlockLifecycleBurstCommand moving = new(
            cell,
            "AutoTest.LogicMoving",
            SendStaleAdd: true);

        LogicEditCommand decodedEdit = ControlProtocol.CreateRequest(
            ControlCommandNames.LogicEdit,
            edit).DeserializePayload<LogicEditCommand>();
        RunMovingBlockLifecycleBurstCommand decodedMoving = ControlProtocol.CreateRequest(
            ControlCommandNames.RunMovingBlockLifecycleBurst,
            moving).DeserializePayload<RunMovingBlockLifecycleBurstCommand>();

        Assert.Equal(9, AutomationProtocol.CurrentApiVersion);
        Assert.Equal(edit, decodedEdit);
        Assert.Equal(moving, decodedMoving);
        Assert.Equal("Logic", AutomationCommandDomain.Logic.ToString());
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void LogicReplayParserRequiresExactSeventeenActionContract() {
        TerrainCellCoordinate battery = new(10, 70, 10);
        TerrainCellCoordinate switchCell = new(12, 70, 10);
        LogicElementKind[] kinds = Enum.GetValues<LogicElementKind>();
        ScenarioActionRecordV2[] records = [
            Action("client-a", "forged", ControlCommandNames.LogicEdit,
                new LogicEditCommand(1, LogicElementKind.Battery, battery, 15,
                    ForgeAuthoritativeState: true, ExpectRejected: true)),
            Action("client-a", "far", ControlCommandNames.LogicEdit,
                new LogicEditCommand(1, LogicElementKind.Battery, new(30, 70, 10), 14,
                    ExpectRejected: true)),
            Action("client-a", "cross", ControlCommandNames.LogicInteract,
                new LogicInteractCommand(1, switchCell, 2, ExpectRejected: true)),
            Action("client-b", "observer", ControlCommandNames.LogicEdit,
                new LogicEditCommand(2, LogicElementKind.Battery, battery, 14,
                    ExpectRejected: true)),
            .. kinds.Select((kind, index) =>
                Action("client-a", $"edit-{index}", ControlCommandNames.LogicEdit,
                    new LogicEditCommand(1, kind, new(10 + index, 70, 12), DuplicateCount: 2))),
            Action("client-a", "toggle", ControlCommandNames.LogicInteract,
                new LogicInteractCommand(1, switchCell, DuplicateCount: 2)),
            Action("server", "revision", ControlCommandNames.RunLogicRevisionBurst,
                new RunLogicRevisionBurstCommand(battery)),
            Action("server", "moving", ControlCommandNames.RunMovingBlockLifecycleBurst,
                new RunMovingBlockLifecycleBurstCommand(new(10, 70, 14))),
            Action("server", "piston", ControlCommandNames.RunPistonLifecycle,
                new RunPistonLifecycleCommand(new(10, 70, 16)))
        ];

        LogicReplayPlan plan = LogicMultiplayerScenario.ParseReplayActions(records);

        Assert.Equal(9, plan.Edits.Length);
        Assert.True(plan.ForgedState.ForgeAuthoritativeState);
        Assert.Equal(2, plan.Toggle.DuplicateCount);
        Assert.True(plan.MovingBurst.SendStaleAdd);
        Assert.Throws<InvalidDataException>(() =>
            LogicMultiplayerScenario.ParseReplayActions([
                records[0] with { Role = "server" },
                .. records[1..]
            ]));
        Assert.Throws<InvalidDataException>(() =>
            LogicMultiplayerScenario.ParseReplayActions([
                .. records[..15],
                records[15] with {
                    Payload = JsonSerializer.SerializeToElement(
                        new RunMovingBlockLifecycleBurstCommand(
                            new(10, 70, 14),
                            SendStaleAdd: false),
                        ControlProtocol.JsonOptions)
                },
                records[16]
            ]));
    }

    private static ScenarioActionRecordV2 Action<TCommand>(
        string role,
        string actionId,
        string command,
        TCommand payload) =>
        new(
            role,
            command,
            JsonSerializer.SerializeToElement(payload, ControlProtocol.JsonOptions),
            new ActionReceipt(actionId, true, 1, role == "server" ? -1 : 1, "applied"));
}
