using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Survivalcraft.Test;

public static class LogicBlockSyncTools {
    public static readonly LogicBlockCase[] EditableSettingCases = [
        new("Battery", 138, 0, 7, 8, 8f, 6f),
        new("Button", 142, 4, 6, 76, 10f, 6f),
        new("Switch", 141, 8, 5, 168, 12f, 6f),
        new("AdjustableDelayGate", 224, 16, 9, 304, 14f, 6f),
        new("Piston", 237, 66, 13634, 13634, 16f, 6f)
    ];

    public static readonly StructuredBlockCase[] StructuredDataCases = [
        new("MemoryBank", 186, 16, "1234ABCD", string.Empty, string.Empty, string.Empty, 8f, 8f),
        new("TruthTable", 188, 16, string.Empty, "F0E1D2C3B4A59687", string.Empty, string.Empty, 10f, 8f),
        new(
            "Sign",
            97,
            0,
            string.Empty,
            string.Empty,
            "North|East|South|West",
            "https://example.test/sign",
            12f,
            8f)
    ];

    public static readonly MechanicalBlockCase PistonMotionCase = new(
        "Piston",
        237,
        12736,
        18f,
        10f);

    public static readonly MechanicalBlockCase DispenserSettingsCase = new(
        "Dispenser",
        216,
        0,
        20f,
        10f);

    public static Task ConnectTestServerAsync(GameContext context, int serverPort) =>
        GameTools.ConnectTestServer(context, serverPort);

    public static async Task<bool> WaitForMainPlayerAsync(GameContext context) {
        WaitResult<bool> result = await TestWaiter.TryUntilAsync(
            async () => {
                object? value = await context.PushCommandAsync(
                    "FUNC return Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>().ComponentPlayers.FirstOrDefault(p => p.PlayerData.IsMainPlayer)?.ComponentMiner != null;");
                return value is bool ready && ready;
            },
            ready => ready,
            TestTimeouts.DefaultEventually);
        return result.Matched;
    }

    public static async Task<LogicBlockState> CreateLogicBlockAsync(GameContext context, LogicBlockCase testCase) {
        string script = GameTools.ReadScript("LogicBlockSync/CreateLogicBlockCode.cs")
            .Replace("__OFFSET_X__", testCase.OffsetX.ToString(CultureInfo.InvariantCulture))
            .Replace("__OFFSET_Z__", testCase.OffsetZ.ToString(CultureInfo.InvariantCulture))
            .Replace("__BLOCK_INDEX__", testCase.BlockIndex.ToString(CultureInfo.InvariantCulture))
            .Replace("__INITIAL_DATA__", testCase.InitialData.ToString(CultureInfo.InvariantCulture));

        string raw = (string?)await context.PushCommandAsync("FUNC " + script) ?? string.Empty;
        return ParseLogicBlockState(raw);
    }

    public static async Task<LogicBlockState> CreateStructuredBlockAsync(GameContext context, StructuredBlockCase testCase) {
        string script = GameTools.ReadScript("LogicBlockSync/CreateLogicBlockCode.cs")
            .Replace("__OFFSET_X__", testCase.OffsetX.ToString(CultureInfo.InvariantCulture))
            .Replace("__OFFSET_Z__", testCase.OffsetZ.ToString(CultureInfo.InvariantCulture))
            .Replace("__BLOCK_INDEX__", testCase.BlockIndex.ToString(CultureInfo.InvariantCulture))
            .Replace("__INITIAL_DATA__", testCase.InitialData.ToString(CultureInfo.InvariantCulture));

        string raw = (string?)await context.PushCommandAsync("FUNC " + script) ?? string.Empty;
        return ParseLogicBlockState(raw);
    }

    public static async Task<LogicBlockState> CreateMechanicalBlockAsync(GameContext context, MechanicalBlockCase testCase) {
        string script = GameTools.ReadScript("LogicBlockSync/CreateLogicBlockCode.cs")
            .Replace("__OFFSET_X__", testCase.OffsetX.ToString(CultureInfo.InvariantCulture))
            .Replace("__OFFSET_Z__", testCase.OffsetZ.ToString(CultureInfo.InvariantCulture))
            .Replace("__BLOCK_INDEX__", testCase.BlockIndex.ToString(CultureInfo.InvariantCulture))
            .Replace("__INITIAL_DATA__", testCase.InitialData.ToString(CultureInfo.InvariantCulture));

        string raw = (string?)await context.PushCommandAsync("FUNC " + script) ?? string.Empty;
        return ParseLogicBlockState(raw);
    }

    public static async Task<string> EditLogicBlockAsync(GameContext context, LogicBlockState target, LogicBlockCase testCase) {
        string script = GameTools.ReadScript("LogicBlockSync/EditLogicBlockCode.cs")
            .Replace("__X__", target.X.ToString(CultureInfo.InvariantCulture))
            .Replace("__Y__", target.Y.ToString(CultureInfo.InvariantCulture))
            .Replace("__Z__", target.Z.ToString(CultureInfo.InvariantCulture))
            .Replace("__KIND__", testCase.Kind)
            .Replace("__EDIT_VALUE__", testCase.EditValue.ToString(CultureInfo.InvariantCulture));

        return (string?)await context.PushCommandAsync("FUNC " + script) ?? string.Empty;
    }

    public static async Task<string> EditStructuredBlockDataAsync(GameContext context, LogicBlockState target, StructuredBlockCase testCase) {
        string script = GameTools.ReadScript("LogicBlockSync/EditStructuredBlockDataCode.cs")
            .Replace("__X__", target.X.ToString(CultureInfo.InvariantCulture))
            .Replace("__Y__", target.Y.ToString(CultureInfo.InvariantCulture))
            .Replace("__Z__", target.Z.ToString(CultureInfo.InvariantCulture))
            .Replace("__KIND__", testCase.Kind)
            .Replace("__MEMORY_DATA__", testCase.ExpectedMemory)
            .Replace("__TRUTH_DATA__", testCase.ExpectedTruth)
            .Replace("__SIGN_LINE_1__", "North")
            .Replace("__SIGN_LINE_2__", "East")
            .Replace("__SIGN_LINE_3__", "South")
            .Replace("__SIGN_LINE_4__", "West")
            .Replace("__SIGN_URL__", testCase.ExpectedSignUrl);

        return (string?)await context.PushCommandAsync("FUNC " + script) ?? string.Empty;
    }

    public static async Task<string> TriggerPistonMotionAsync(GameContext context, LogicBlockState target, int length) {
        string script = GameTools.ReadScript("LogicBlockSync/TriggerPistonMotionCode.cs")
            .Replace("__X__", target.X.ToString(CultureInfo.InvariantCulture))
            .Replace("__Y__", target.Y.ToString(CultureInfo.InvariantCulture))
            .Replace("__Z__", target.Z.ToString(CultureInfo.InvariantCulture))
            .Replace("__LENGTH__", length.ToString(CultureInfo.InvariantCulture));

        return (string?)await context.PushCommandAsync("FUNC " + script) ?? string.Empty;
    }

    public static async Task<PistonPushBlockState> PlacePistonPushBlockAsync(GameContext context, LogicBlockState target) {
        string script = GameTools.ReadScript("LogicBlockSync/PlacePistonPushBlockCode.cs")
            .Replace("__X__", target.X.ToString(CultureInfo.InvariantCulture))
            .Replace("__Y__", target.Y.ToString(CultureInfo.InvariantCulture))
            .Replace("__Z__", target.Z.ToString(CultureInfo.InvariantCulture));

        string raw = (string?)await context.PushCommandAsync("FUNC " + script) ?? string.Empty;
        return ParsePistonPushBlockState(raw);
    }

    public static async Task<string> StopPistonMotionAsync(GameContext context, LogicBlockState target) {
        string script = GameTools.ReadScript("LogicBlockSync/StopPistonMotionCode.cs")
            .Replace("__X__", target.X.ToString(CultureInfo.InvariantCulture))
            .Replace("__Y__", target.Y.ToString(CultureInfo.InvariantCulture))
            .Replace("__Z__", target.Z.ToString(CultureInfo.InvariantCulture));

        return (string?)await context.PushCommandAsync("FUNC " + script) ?? string.Empty;
    }

    public static async Task<string> EditDispenserSettingsAsync(GameContext context, LogicBlockState target) {
        string script = GameTools.ReadScript("LogicBlockSync/EditDispenserSettingsCode.cs")
            .Replace("__X__", target.X.ToString(CultureInfo.InvariantCulture))
            .Replace("__Y__", target.Y.ToString(CultureInfo.InvariantCulture))
            .Replace("__Z__", target.Z.ToString(CultureInfo.InvariantCulture));

        return (string?)await context.PushCommandAsync("FUNC " + script) ?? string.Empty;
    }

    public static async Task<VoltageState> CreateCounterVoltageCircuitAsync(GameContext context) {
        string script = GameTools.ReadScript("LogicBlockSync/CreateCounterVoltageCircuitCode.cs");

        string raw = (string?)await context.PushCommandAsync("FUNC " + script) ?? string.Empty;
        return ParseVoltageState(raw);
    }

    public static async Task<string> SetCounterInputAsync(GameContext context, VoltageState target, bool powered) {
        string script = GameTools.ReadScript("LogicBlockSync/SetCounterInputCode.cs")
            .Replace("__X__", target.X.ToString(CultureInfo.InvariantCulture))
            .Replace("__Y__", target.Y.ToString(CultureInfo.InvariantCulture))
            .Replace("__Z__", target.Z.ToString(CultureInfo.InvariantCulture))
            .Replace("__POWERED__", powered ? "true" : "false");

        return (string?)await context.PushCommandAsync("FUNC " + script) ?? string.Empty;
    }

    public static async Task<string> RemoveCounterVoltageCircuitAsync(GameContext context, VoltageState target) {
        string script = GameTools.ReadScript("LogicBlockSync/RemoveCounterVoltageCircuitCode.cs")
            .Replace("__X__", target.X.ToString(CultureInfo.InvariantCulture))
            .Replace("__Y__", target.Y.ToString(CultureInfo.InvariantCulture))
            .Replace("__Z__", target.Z.ToString(CultureInfo.InvariantCulture));

        return (string?)await context.PushCommandAsync("FUNC " + script) ?? string.Empty;
    }

    public static async Task<LogicBlockState> GetLogicBlockStateAsync(GameContext context, LogicBlockState target) {
        string script = GameTools.ReadScript("LogicBlockSync/GetLogicBlockStateCode.cs")
            .Replace("__X__", target.X.ToString(CultureInfo.InvariantCulture))
            .Replace("__Y__", target.Y.ToString(CultureInfo.InvariantCulture))
            .Replace("__Z__", target.Z.ToString(CultureInfo.InvariantCulture));

        string raw = (string?)await context.PushCommandAsync("FUNC " + script) ?? string.Empty;
        return ParseLogicBlockState(raw);
    }

    public static async Task<StructuredBlockState> GetStructuredBlockStateAsync(GameContext context, LogicBlockState target) {
        string script = GameTools.ReadScript("LogicBlockSync/GetStructuredBlockStateCode.cs")
            .Replace("__X__", target.X.ToString(CultureInfo.InvariantCulture))
            .Replace("__Y__", target.Y.ToString(CultureInfo.InvariantCulture))
            .Replace("__Z__", target.Z.ToString(CultureInfo.InvariantCulture));

        string raw = (string?)await context.PushCommandAsync("FUNC " + script) ?? string.Empty;
        return ParseStructuredBlockState(raw);
    }

    public static async Task<MechanicalBlockState> GetMechanicalBlockStateAsync(GameContext context, LogicBlockState target) {
        string script = GameTools.ReadScript("LogicBlockSync/GetMechanicalBlockStateCode.cs")
            .Replace("__X__", target.X.ToString(CultureInfo.InvariantCulture))
            .Replace("__Y__", target.Y.ToString(CultureInfo.InvariantCulture))
            .Replace("__Z__", target.Z.ToString(CultureInfo.InvariantCulture));

        string raw = (string?)await context.PushCommandAsync("FUNC " + script) ?? string.Empty;
        return ParseMechanicalBlockState(raw);
    }

    public static async Task<VoltageState> GetVoltageStateAsync(GameContext context, VoltageState target) {
        string script = GameTools.ReadScript("LogicBlockSync/GetPersistentVoltageStateCode.cs")
            .Replace("__X__", target.X.ToString(CultureInfo.InvariantCulture))
            .Replace("__Y__", target.Y.ToString(CultureInfo.InvariantCulture))
            .Replace("__Z__", target.Z.ToString(CultureInfo.InvariantCulture));

        string raw = (string?)await context.PushCommandAsync("FUNC " + script) ?? string.Empty;
        return ParseVoltageState(raw);
    }

    public static async Task<LogicBlockState> WaitForLogicBlockStateAsync(
        GameContext context,
        LogicBlockState target,
        int expectedContents,
        int expectedData) {
        return await WaitForStateOrLastAsync(
            () => GetLogicBlockStateAsync(context, target),
            state => state.Contents == expectedContents
                && state.Data == expectedData);
    }

    public static async Task<StructuredBlockState> WaitForStructuredBlockStateAsync(
        GameContext context,
        LogicBlockState target,
        StructuredBlockCase testCase) {
        return await WaitForStateOrLastAsync(
            () => GetStructuredBlockStateAsync(context, target),
            state => StructuredBlockStatesMatch(state, testCase));
    }

    public static async Task<MechanicalBlockState> WaitForPistonMovingAsync(GameContext context, LogicBlockState target) {
        return await WaitForStateOrLastAsync(
            () => GetMechanicalBlockStateAsync(context, target),
            state => state.PistonMoving > 0,
            timeout: TestTimeouts.DefaultEventually);
    }

    public static async Task<MechanicalBlockState> WaitForNoPistonMovingAsync(GameContext context, LogicBlockState target) {
        return await WaitForStateOrLastAsync(
            () => GetMechanicalBlockStateAsync(context, target),
            state => state.PistonMoving == 0);
    }

    public static async Task<MechanicalBlockState> WaitForDispenserStateAsync(GameContext context, LogicBlockState target, int expectedData) {
        return await WaitForStateOrLastAsync(
            () => GetMechanicalBlockStateAsync(context, target),
            state => state.Contents == DispenserSettingsCase.BlockIndex
                && state.Data == expectedData
                && state.DispenserEntity);
    }

    public static async Task<VoltageState> WaitForVoltageStateAsync(GameContext context, VoltageState target) {
        return await WaitForStateOrLastAsync(
            () => GetVoltageStateAsync(context, target),
            state => state.Contents == CounterBlockIndex);
    }

    public static async Task<VoltageState> WaitForCounterVoltageAsync(GameContext context, VoltageState target, int expectedCounter) {
        return await WaitForStateOrLastAsync(
            () => GetVoltageStateAsync(context, target),
            state => state.Contents == CounterBlockIndex
                && state.Counter == expectedCounter
                && state.HasPersistent
                && VoltageEquals(state.PersistentVoltage, expectedCounter / 15f));
    }

    public static async Task<VoltageState> WaitForCounterReadyForPulseAsync(GameContext context, VoltageState target, int expectedCounter) {
        return await WaitForStateOrLastAsync(
            () => GetVoltageStateAsync(context, target),
            state => state.Contents == CounterBlockIndex
                && state.SourceContents == 0
                && state.Counter == expectedCounter
                && state.PlusAllowed);
    }

    public static async Task<VoltageState> WaitForPersistentVoltageAsync(GameContext context, VoltageState target, int expectedCounter) {
        return await WaitForStateOrLastAsync(
            () => GetVoltageStateAsync(context, target),
            state => state.Contents == CounterBlockIndex
                && state.HasPersistent
                && VoltageEquals(state.PersistentVoltage, expectedCounter / 15f));
    }

    public static async Task<VoltageState> WaitForVoltageRemovedAsync(GameContext context, VoltageState target) {
        return await WaitForStateOrLastAsync(
            () => GetVoltageStateAsync(context, target),
            state => state.Contents != CounterBlockIndex
                && state.SourceContents == 0
                && !state.HasPersistent);
    }

    public static async Task<bool> WaitForScreenAsync(GameContext context, string screenTypeName) {
        WaitResult<string> result = await TestWaiter.TryUntilAsync(
            async () => (string?)await context.PushCommandAsync(
                "FUNC return Game.ScreensManager.CurrentScreen?.GetType().Name ?? \"\";") ?? string.Empty,
            screen => string.Equals(screen, screenTypeName, StringComparison.Ordinal),
            TestTimeouts.DefaultEventually);
        return result.Matched;
    }

    public static async Task<int> WaitForClientPlayerIndexAsync(GameContext context) {
        WaitResult<int> result = await TestWaiter.TryUntilAsync(
            async () => {
                object? value = await context.PushCommandAsync("FUNC return Game.NetworkManager.PlayerIndex;");
                return value is int id ? id : Convert.ToInt32(value, CultureInfo.InvariantCulture);
            },
            playerIndex => playerIndex >= 0,
            TestTimeouts.DefaultEventually);
        return result.Matched ? result.Last : -1;
    }

    private static async Task<T> WaitForStateOrLastAsync<T>(
        Func<Task<T>> probe,
        Func<T, bool> predicate,
        TimeSpan? timeout = null) {
        WaitResult<T> result = await TestWaiter.TryUntilAsync(
            probe,
            predicate,
            timeout ?? TestTimeouts.DefaultEventually);
        return result.Last;
    }

    public static LogicBlockState ParseLogicBlockState(string value) {
        Dictionary<string, string> values = ParseKeyValuePairs(value);
        return new LogicBlockState(
            ParseInt(values, "x"),
            ParseInt(values, "y"),
            ParseInt(values, "z"),
            ParseInt(values, "contents"),
            ParseInt(values, "data"),
            value);
    }

    public static StructuredBlockState ParseStructuredBlockState(string value) {
        Dictionary<string, string> values = ParseKeyValuePairs(value);
        return new StructuredBlockState(
            ParseInt(values, "x"),
            ParseInt(values, "y"),
            ParseInt(values, "z"),
            ParseInt(values, "contents"),
            ParseInt(values, "data"),
            values.TryGetValue("memory", out string? memory) ? memory : string.Empty,
            values.TryGetValue("truth", out string? truth) ? truth : string.Empty,
            values.TryGetValue("signLines", out string? signLines) ? signLines : string.Empty,
            values.TryGetValue("signColors", out string? signColors) ? signColors : string.Empty,
            values.TryGetValue("signUrl", out string? signUrl) ? signUrl : string.Empty,
            value);
    }

    public static MechanicalBlockState ParseMechanicalBlockState(string value) {
        Dictionary<string, string> values = ParseKeyValuePairs(value);
        return new MechanicalBlockState(
            ParseInt(values, "x"),
            ParseInt(values, "y"),
            ParseInt(values, "z"),
            ParseInt(values, "contents"),
            ParseInt(values, "data"),
            ParseInt(values, "pistonMoving"),
            values.TryGetValue("pistonBlocks", out string? pistonBlocks) ? pistonBlocks : string.Empty,
            ParseBool(values, "dispenserEntity"),
            value);
    }

    public static VoltageState ParseVoltageState(string value) {
        Dictionary<string, string> values = ParseKeyValuePairs(value);
        bool hasPersistent = ParseBool(values, "hasPersistent");
        return new VoltageState(
            ParseInt(values, "x"),
            ParseInt(values, "y"),
            ParseInt(values, "z"),
            ParseInt(values, "contents"),
            ParseInt(values, "data"),
            ParseInt(values, "sourceContents"),
            ParseInt(values, "sourceData"),
            hasPersistent,
            hasPersistent ? ParseFloat(values, "persistent") : 0f,
            ParseFloat(values, "output"),
            ParseInt(values, "counter"),
            ParseBool(values, "overflow"),
            ParseBool(values, "plusAllowed"),
            ParseInt(values, "circuitStep"),
            value);
    }

    public static PistonPushBlockState ParsePistonPushBlockState(string value) {
        Dictionary<string, string> values = ParseKeyValuePairs(value);
        string offset = values.TryGetValue("offset", out string? parsedOffset) ? parsedOffset : string.Empty;
        int blockValue = ParseInt(values, "value");
        return new PistonPushBlockState(offset, blockValue, $"{offset}:{blockValue}", value);
    }

    public static bool StructuredBlockStatesMatch(StructuredBlockState state, StructuredBlockCase testCase) {
        if (state.Contents != testCase.BlockIndex
            || state.Data != testCase.InitialData) {
            return false;
        }

        return testCase.Kind switch {
            "MemoryBank" => state.Memory == testCase.ExpectedMemory,
            "TruthTable" => state.Truth == testCase.ExpectedTruth,
            "Sign" => state.SignLines == testCase.ExpectedSignLines
                && state.SignColors == testCase.ExpectedSignColors
                && state.SignUrl == testCase.ExpectedSignUrl,
            _ => false
        };
    }

    private static Dictionary<string, string> ParseKeyValuePairs(string value) =>
        value
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => p[1], StringComparer.OrdinalIgnoreCase);

    private static int ParseInt(Dictionary<string, string> values, string key) =>
        values.TryGetValue(key, out string? value)
        && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
            ? result
            : 0;

    private static bool ParseBool(Dictionary<string, string> values, string key) =>
        values.TryGetValue(key, out string? value)
        && bool.TryParse(value, out bool result)
        && result;

    private static float ParseFloat(Dictionary<string, string> values, string key) =>
        values.TryGetValue(key, out string? value)
        && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result)
            ? result
            : 0f;

    private static bool VoltageEquals(float actual, float expected) =>
        Math.Abs(actual - expected) <= 0.001f;

    public const int CounterBlockIndex = 184;

    public readonly record struct LogicBlockCase(
        string Kind,
        int BlockIndex,
        int InitialData,
        int EditValue,
        int ExpectedData,
        float OffsetX,
        float OffsetZ);

    public readonly record struct StructuredBlockCase(
        string Kind,
        int BlockIndex,
        int InitialData,
        string ExpectedMemory,
        string ExpectedTruth,
        string ExpectedSignLines,
        string ExpectedSignUrl,
        float OffsetX,
        float OffsetZ) {
        public string ExpectedSignColors => "4278190335|4278255360|4294901760|4294967295";
    }

    public readonly record struct LogicBlockState(
        int X,
        int Y,
        int Z,
        int Contents,
        int Data,
        string Raw);

    public readonly record struct MechanicalBlockCase(
        string Kind,
        int BlockIndex,
        int InitialData,
        float OffsetX,
        float OffsetZ);

    public readonly record struct StructuredBlockState(
        int X,
        int Y,
        int Z,
        int Contents,
        int Data,
        string Memory,
        string Truth,
        string SignLines,
        string SignColors,
        string SignUrl,
        string Raw);

    public readonly record struct MechanicalBlockState(
        int X,
        int Y,
        int Z,
        int Contents,
        int Data,
        int PistonMoving,
        string PistonBlocks,
        bool DispenserEntity,
        string Raw);

    public readonly record struct PistonPushBlockState(
        string Offset,
        int Value,
        string Signature,
        string Raw);

    public readonly record struct VoltageState(
        int X,
        int Y,
        int Z,
        int Contents,
        int Data,
        int SourceContents,
        int SourceData,
        bool HasPersistent,
        float PersistentVoltage,
        float OutputVoltage,
        int Counter,
        bool Overflow,
        bool PlusAllowed,
        int CircuitStep,
        string Raw);
}
