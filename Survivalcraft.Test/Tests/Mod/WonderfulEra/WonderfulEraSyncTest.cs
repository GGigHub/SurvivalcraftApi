using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Survivalcraft.Test;

// WonderfulEra + CompatNet 联机同步测试。
// 目标：客户端动作 -> 服务端权威 -> 两端读指纹对比，如实记录哪些玩法同步、哪些不同步。
[TestCaseOrderer("Survivalcraft.Test.PriorityOrderer", "Survivalcraft.Test")]
public sealed class WonderfulEraSyncTest : IClassFixture<WonderfulEraGameFixture>, IDisposable {
    private const float VitalStatsSyncEpsilon = 0.15f;

    private readonly WonderfulEraGameFixture fixture;
    private readonly ITestOutputHelper output;
    private GameContext server => fixture.Server;
    private GameContext client => fixture.Client;

    public WonderfulEraSyncTest(WonderfulEraGameFixture fixture, ITestOutputHelper output) {
        this.fixture = fixture;
        this.output = output;
    }

    [Fact, TestOrder(Priority = 1)]
    public async Task WonderfulEraAndCompatNetLoadAndVehicleTemplateIsUsable() {
        await EnsureWorldAndClientAsync();

        List<string> serverMods = await ReadModPackageListAsync(server);
        List<string> clientMods = await ReadModPackageListAsync(client);
        output.WriteLine($"[记录] 服务端 mod 包名: {string.Join(",", serverMods)}");
        output.WriteLine($"[记录] 客户端 mod 包名: {string.Join(",", clientMods)}");
        Assert.Contains(serverMods, p => p.Contains("compatnet", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(serverMods, p => p.Contains("wonderfulera", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(clientMods, p => p.Contains("compatnet", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(clientMods, p => p.Contains("wonderfulera", StringComparison.OrdinalIgnoreCase));

        string templateInfo = (string?)await server.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("WonderfulEra/GetVehicleTemplateInfoCode.cs")) ?? "missing";
        output.WriteLine($"[记录] AE86_Car 模板指纹(服务端): {templateInfo}");

        VehicleTemplateInfo info = ParseVehicleTemplateInfo(templateInfo);
        Assert.True(info.Created, $"AE86_Car 模板无法创建: {templateInfo}");
        Assert.True(info.HasVehicle, $"AE86_Car 缺少 ComponentVehicle: {templateInfo}");
        Assert.True(info.HasVehicleInventory, $"AE86_Car 缺少 ComponentVehicleInventory: {templateInfo}");
        Assert.True(info.HasBlockEntity, $"AE86_Car 缺少 ComponentNewBlockEntity: {templateInfo}");
        Assert.True(info.HasMount, $"AE86_Car 缺少 ComponentMount: {templateInfo}");
        Assert.True(info.SlotsCount >= 3, $"AE86_Car 油箱/残留槽位缺失: {templateInfo}");
        output.WriteLine($"[记录] AE86_Car 含 ComponentCreature={info.HasCreature}（决定服务端->客户端身体同步是否可用）");
    }

    [Fact, TestOrder(Priority = 2)]
    public async Task AE86CarPlacementSynchronizesToClient() {
        await EnsureWorldAndClientAsync();
        int playerIndex = await PlayerSyncTools.WaitForClientPlayerIndexAsync(client);

        int vehicleId = await CreateVehicleAsync(server, playerIndex, offsetX: 2, offsetY: 0, offsetZ: 2, gravity: false);
        Assert.True(vehicleId > 0, "服务端未能创建 AE86_Car。");
        output.WriteLine($"[记录] 服务端创建 AE86_Car 实体 id={vehicleId}（无重力，静止）");

        Assert.True(await WaitForEntityExistsAsync(client, vehicleId), $"客户端未收到实体 {vehicleId}。");
        output.WriteLine($"[记录] 客户端已收到实体 {vehicleId}");

        BodySyncState serverBody = await CreatureSyncTools.GetBodyStateAsync(server, vehicleId);
        BodySyncState clientBody = await WaitForClientBodyNearAsync(client, vehicleId, serverBody, timeoutSeconds: 10);
        output.WriteLine($"[记录] 放置同步 服务端={serverBody}");
        output.WriteLine($"[记录] 放置同步 客户端={clientBody}");
    }

    [Fact, TestOrder(Priority = 3)]
    public async Task ClientDriveControlRelaysToServerAndBodyFollows() {
        await EnsureWorldAndClientAsync();
        int playerIndex = await PlayerSyncTools.WaitForClientPlayerIndexAsync(client);

        int vehicleId = await CreateVehicleAsync(server, playerIndex, offsetX: 3, offsetY: 1, offsetZ: 3, gravity: true);
        Assert.True(vehicleId > 0, "服务端未能创建 AE86_Car。");
        output.WriteLine($"[记录] 服务端创建 AE86_Car(有重力) 实体 id={vehicleId}");

        await WaitForVehicleSettledAsync(server, vehicleId);
        output.WriteLine("[记录] 服务端车辆已落地(standing=1)");

        string prepare = (string?)await server.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("WonderfulEra/PrepareVehicleCode.cs").Replace("__ENTITY_ID__", vehicleId.ToString(CultureInfo.InvariantCulture))) ?? "missing";
        Assert.Equal("ok", prepare);
        output.WriteLine("[记录] 服务端车辆已准备(点火/满能源/加汽油)");

        Assert.True(await WaitForEntityExistsAsync(client, vehicleId), $"客户端未收到实体 {vehicleId}。");
        output.WriteLine($"[记录] 客户端已收到实体 {vehicleId}");

        MechanicsSyncTools.MountState mount = await MechanicsSyncTools.StartMountingAsync(client, playerIndex, vehicleId);
        Assert.True(mount.Exists, $"客户端开始骑乘失败: {mount.Raw}");
        output.WriteLine($"[记录] 客户端骑乘请求: {mount.Raw}");
        await MechanicsSyncTools.WaitForMountStateAsync(client, playerIndex, vehicleId);
        await MechanicsSyncTools.WaitForMountStateAsync(server, playerIndex, vehicleId);
        output.WriteLine("[记录] 服务端与客户端均已确认骑乘(坐骑同步正常)");

        VehicleState before = await GetVehicleStateAsync(server, vehicleId);
        output.WriteLine($"[记录] 前进前服务端车辆状态: {before}");

        await CreatureSyncTools.HoldMainPlayerMoveInputAsync(client, new Vector3Dto(0, 0, 1), frameCount: 300);
        output.WriteLine("[记录] 客户端已按住前进 300 帧 MOVE_INPUT");

        VehicleState after = await WaitForServerVehicleMovedAsync(server, vehicleId, before, timeoutSeconds: 15);
        output.WriteLine($"[记录] 前进后服务端车辆状态: {after}");
        Assert.True(
            VehicleHorizontalSpeed(after.Position) > 0.3f
            || Vector3Dto.HorizontalDistanceSquared(before.Position, after.Position) > 1.0f,
            "客户端按住前进后服务端车辆没有移动，CompatNet 车辆控制链路(客户端→服务端)可能未生效。"
            + $" 前={before}，后={after}");

        BodySyncState serverBody = await CreatureSyncTools.GetBodyStateAsync(server, vehicleId);
        BodySyncState clientBody = await WaitForClientBodyNearAsync(client, vehicleId, serverBody, timeoutSeconds: 30);
        output.WriteLine($"[记录] 驾驶后身体同步 服务端={serverBody}");
        output.WriteLine($"[记录] 驾驶后身体同步 客户端={clientBody}");

        VehicleState serverFinal = await GetVehicleStateAsync(server, vehicleId);
        VehicleState clientFinal = await GetVehicleStateAsync(client, vehicleId);
        output.WriteLine($"[记录] 驾驶结束 服务端: {serverFinal}");
        output.WriteLine($"[记录] 驾驶结束 客户端: {clientFinal}");

        MechanicsSyncTools.MountState dismount = await MechanicsSyncTools.StartDismountingAsync(client, playerIndex);
        Assert.True(dismount.Exists, $"客户端下车请求失败: {dismount.Raw}");
        output.WriteLine($"[记录] 客户端下车请求: {dismount.Raw}");
        await MechanicsSyncTools.WaitForMountStateAsync(server, playerIndex, 0);
        await MechanicsSyncTools.WaitForMountStateAsync(client, playerIndex, 0);
        output.WriteLine("[记录] WonderfulEra 下车请求与服务端确认已同步。");
        output.WriteLine("[记录] 车辆 run/energy/fuel 为服务端本地数据（无专门同步包），仅记录不强制断言");
    }

    [Fact, TestOrder(Priority = 4)]
    public async Task RefrigeratorInventorySynchronizesAndPowerStateRecorded() {
        await EnsureWorldAndClientAsync();

        int fridgeId = await PlaceRefrigeratorAsync(server, offsetX: 6, offsetY: 2, offsetZ: 6);
        Assert.True(fridgeId > 0, "服务端未能放置冰箱方块实体。");
        output.WriteLine($"[记录] 服务端放置冰箱 方块实体 id={fridgeId}");

        Assert.True(await WaitForEntityExistsAsync(client, fridgeId), $"客户端未收到冰箱实体 {fridgeId}。");
        output.WriteLine($"[记录] 客户端已收到冰箱实体 {fridgeId}");

        string seeded = (string?)await server.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("WonderfulEra/SeedRefrigeratorCode.cs").Replace("__ENTITY_ID__", fridgeId.ToString(CultureInfo.InvariantCulture))) ?? "missing";
        Assert.Equal("seeded", seeded);

        RefrigeratorState serverState = await WaitForRefrigeratorSlottedAsync(server, fridgeId);
        RefrigeratorState clientState = await WaitForRefrigeratorSlottedAsync(client, fridgeId);
        Assert.Equal(serverState.SlotsCount, clientState.SlotsCount);
        Assert.Equal(serverState.Slot0Contents, clientState.Slot0Contents);
        Assert.Equal(serverState.Slot0Count, clientState.Slot0Count);
        output.WriteLine($"[记录] 冰箱库存同步 服务端={serverState.Inventory}");
        output.WriteLine($"[记录] 冰箱库存同步 客户端={clientState.Inventory}");

        await server.PushCommandAsync(
            "EXEC " + GameTools.ReadScript("WonderfulEra/SetRefrigeratorPowerCode.cs")
                .Replace("__ENTITY_ID__", fridgeId.ToString(CultureInfo.InvariantCulture))
                .Replace("__POWER__", "true"));
        await Task.Delay(1500);

        serverState = await GetRefrigeratorStateAsync(server, fridgeId);
        clientState = await GetRefrigeratorStateAsync(client, fridgeId);
        output.WriteLine($"[记录] 冰箱 PowerOn 服务端(权威)={serverState.PowerOn},客户端={clientState.PowerOn}");
        output.WriteLine($"[记录] 冰箱 m_powerOn 服务端={serverState.MPowerOn},客户端={clientState.MPowerOn}");
        Assert.Equal(serverState.Slot0Contents, clientState.Slot0Contents);
        Assert.Equal(serverState.Slot0Count, clientState.Slot0Count);

        Assert.Equal(serverState.PowerOn, clientState.PowerOn);
        Assert.Equal(serverState.MPowerOn, clientState.MPowerOn);
        output.WriteLine("[记录] PowerOn/m_powerOn 已通过 WonderfulEra 标量快照同步。");
    }

    [Fact, TestOrder(Priority = 5)]
    public async Task PlayerThirstySynchronizesThroughCompatNetSnapshot() {
        await EnsureWorldAndClientAsync();
        int playerIndex = await PlayerSyncTools.WaitForClientPlayerIndexAsync(client);
        await PlayerSyncTools.WaitForPlayerEntityAsync(server, playerIndex);

        string result = (string?)await server.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("WonderfulEra/SetPlayerThirstyCode.cs")
                .Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture))
                .Replace("__THIRSTY__", "0.17f")) ?? "missing";
        Assert.NotEqual("missing", result);
        Assert.NotEqual("field-missing", result);
        output.WriteLine($"[记录] 服务端将 WonderfulEra Thirsty 设为 {result}");

        DietState serverState = default;
        DietState clientState = default;
        DateTime deadline = DateTime.UtcNow.AddSeconds(12);
        while (DateTime.UtcNow < deadline) {
            serverState = await GetPlayerDietStateAsync(server, playerIndex);
            clientState = await GetPlayerDietStateAsync(client, playerIndex);
            if (serverState.Exists
                && clientState.Exists
                && Math.Abs(serverState.Thirsty - clientState.Thirsty) <= VitalStatsSyncEpsilon) {
                output.WriteLine($"[记录] Thirsty 已收敛，服务端={serverState.Thirsty:R},客户端={clientState.Thirsty:R}");
                return;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException(
            $"WonderfulEra Thirsty 未在限定时间内同步。服务端={serverState},客户端={clientState}");
    }

    [Fact, TestOrder(Priority = 5)]
    public async Task EatingThroughDragDropPathSynchronizesVitalStats() {
        await EnsureWorldAndClientAsync();
        int playerIndex = await PlayerSyncTools.WaitForClientPlayerIndexAsync(client);
        await PlayerSyncTools.WaitForPlayerEntityAsync(server, playerIndex);

        await InventoryTestTools.WaitForPlayerInventoryAsync(server, playerIndex);
        await InventoryTestTools.WaitForPlayerInventoryAsync(client, playerIndex);

        string modeResult = (string?)await server.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("WonderfulEra/SetWorldGameModeCode.cs")) ?? "missing";
        Assert.Equal("Survival", modeResult);
        output.WriteLine("[记录] 已把世界切换到 Survival 模式，使食物可被设为 0.2 并保持");

        await HealthSyncTools.SetPlayerVitalStatsAsync(server, playerIndex, food: 0.2f, stamina: 1f, sleep: 1f, wetness: 0f, temperature: 1f);
        await HealthSyncTools.WaitForPlayerVitalStatsAsync(client, playerIndex, food: 0.2f, stamina: 1f, sleep: 1f, wetness: 0f, temperature: 1f);
        output.WriteLine("[记录] 已把服务端玩家饥饿设为 0.2 并同步到客户端");

        string seeded = (string?)await server.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("WonderfulEra/SeedPlayerAppleCode.cs").Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture))) ?? "missing";
        Assert.Equal("seeded", seeded);
        object? appleIndexResult = await server.PushCommandAsync("FUNC return Game.BlocksManager.GetBlockIndex<WonderfulEra.AppleBlock>(false, false);");
        int appleContents = appleIndexResult is int appleId ? appleId : Convert.ToInt32(appleIndexResult, CultureInfo.InvariantCulture);
        await InventoryTestTools.AssertInventoryEventuallyAsync(client, playerIndex, $"0:{appleContents}:8");
        output.WriteLine($"[记录] 客户端玩家库存 slot0={appleContents}:8（苹果已就位）");

        DietState dietServerBefore = await GetPlayerDietStateAsync(server, playerIndex);
        DietState dietClientBefore = await GetPlayerDietStateAsync(client, playerIndex);
        output.WriteLine($"[记录] 进食前 服务端={dietServerBefore}");
        output.WriteLine($"[记录] 进食前 客户端={dietClientBefore}");

        string drag = (string?)await client.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("WonderfulEra/EatInClothingSlotCode.cs")
                .Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture))
                .Replace("__FROM_SLOT__", "0")
                .Replace("__TO_SLOT__", "0")) ?? "missing";
        Assert.True(drag.StartsWith("queued:", StringComparison.Ordinal), $"客户端拖拽进食未执行: {drag}");
        output.WriteLine($"[记录] 客户端拖拽(玩家库存slot0 -> 衣物槽slot0)结果: {drag}");

        DietState dietServerAfter = await WaitForServerAteAsync(server, playerIndex, dietServerBefore);
        output.WriteLine($"[记录] 进食后(服务端权威) 服务端={dietServerAfter}");

        DietState dietClientAfter = await WaitForClientDietConvergedAsync(client, playerIndex, dietServerAfter);
        output.WriteLine($"[记录] 进食后(最终) 客户端={dietClientAfter}");

        Assert.True(dietServerAfter.Food > 0.5f, $"服务端进食后 Food 未上升: {dietServerAfter}");
        Assert.True(
            Math.Abs(dietServerAfter.Food - dietClientAfter.Food) <= VitalStatsSyncEpsilon,
            $"Food 未同步。服务端={dietServerAfter.Food:R},客户端={dietClientAfter.Food:R}");
        output.WriteLine($"[记录] Food 同步差异={Math.Abs(dietServerAfter.Food - dietClientAfter.Food):R}（VitalStatSyncPacket 同步）");
        Assert.True(
            Math.Abs(dietServerAfter.Thirsty - dietClientAfter.Thirsty) <= VitalStatsSyncEpsilon,
            $"WonderfulEra Thirsty 未同步。服务端={dietServerAfter.Thirsty:R},客户端={dietClientAfter.Thirsty:R}");
        Assert.Equal(dietServerAfter.Sick, dietClientAfter.Sick);
        Assert.True(
            Math.Abs(dietServerAfter.SicknessDuration - dietClientAfter.SicknessDuration) <= 2f,
            $"SicknessDuration 未同步。服务端={dietServerAfter.SicknessDuration:R},客户端={dietClientAfter.SicknessDuration:R}");
        output.WriteLine($"[记录] Thirsty 同步差异={Math.Abs(dietServerAfter.Thirsty - dietClientAfter.Thirsty):R}（WonderfulEra 标量快照）");
        output.WriteLine($"[记录] Sickness 服务端={dietServerAfter.Sick}({dietServerAfter.SicknessDuration:R}s),客户端={dietClientAfter.Sick}({dietClientAfter.SicknessDuration:R}s)（ComponentSicknessPacket）");
    }

    public void Dispose() {
        fixture.ThrowIfAnyContextHasError();
    }

    private async Task EnsureWorldAndClientAsync() {
        if (!await IsProjectReadyAsync(server)) {
            await PlayerSyncTools.CreateCreativeTestWorldAsync(server, "wonderfulera-sync");
        }

        if (!await IsProjectReadyAsync(client)) {
            await GameTools.ConnectTestServer(client, server.GamePort);
        }

        int playerIndex = await PlayerSyncTools.WaitForClientPlayerIndexAsync(client);
        await PlayerSyncTools.WaitForPlayerEntityAsync(server, playerIndex);
    }

    private static async Task<bool> IsProjectReadyAsync(GameContext context) {
        for (int i = 0; i < 60; i++) {
            try {
                object? result = await context.PushCommandAsync("FUNC return Game.GameManager.Project != null;");
                if (result is bool ready && ready) {
                    return true;
                }
            }
            catch (SocketException) {
            }
            catch (TimeoutException) {
            }

            await Task.Delay(500);
        }

        return false;
    }

    private static async Task<List<string>> ReadModPackageListAsync(GameContext context) {
        object? result = await context.PushCommandAsync("FUNC " + GameTools.ReadScript("WonderfulEra/GetModInfoCode.cs"));
        string raw = result as string ?? string.Empty;
        return raw.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    private static async Task<int> CreateVehicleAsync(GameContext context, int playerIndex, float offsetX, float offsetY, float offsetZ, bool gravity) {
        string script = GameTools.ReadScript("WonderfulEra/CreateVehicleCode.cs")
            .Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture))
            .Replace("__OFFSET_X__", offsetX.ToString(CultureInfo.InvariantCulture))
            .Replace("__OFFSET_Y__", offsetY.ToString(CultureInfo.InvariantCulture))
            .Replace("__OFFSET_Z__", offsetZ.ToString(CultureInfo.InvariantCulture))
            .Replace("__GRAVITY__", gravity ? "true" : "false");
        object? result = await context.PushCommandAsync("FUNC " + script);
        return result is int id ? id : Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private static async Task<bool> WaitForEntityExistsAsync(GameContext context, int entityId) {
        for (int i = 0; i < 80; i++) {
            object? result = await context.PushCommandAsync($"FUNC return Game.GameManager.Project.FindEntity({entityId}) != null;");
            if (result is bool exists && exists) {
                return true;
            }

            await Task.Delay(100);
        }

        return false;
    }

    private static async Task WaitForVehicleSettledAsync(GameContext context, int entityId) {
        VehicleState state = default;
        for (int i = 0; i < 120; i++) {
            state = await GetVehicleStateAsync(context, entityId);
            if (state.Exists && state.Standing) {
                return;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"车辆 {entityId} 未在服务端落地。最后状态={state}。");
    }

    private static async Task<VehicleState> WaitForServerVehicleMovedAsync(GameContext context, int entityId, VehicleState from, int timeoutSeconds) {
        DateTime limit = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        VehicleState state = default;
        while (DateTime.UtcNow < limit) {
            state = await GetVehicleStateAsync(context, entityId);
            if (state.Exists
                && (VehicleHorizontalSpeed(state.Position) > 0.3f || Vector3Dto.HorizontalDistanceSquared(from.Position, state.Position) > 1.0f)) {
                return state;
            }

            await Task.Delay(100);
        }

        return state;
    }

    private static async Task<BodySyncState> WaitForClientBodyNearAsync(GameContext context, int entityId, BodySyncState expected, int timeoutSeconds) {
        DateTime limit = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        BodySyncState state = default;
        while (DateTime.UtcNow < limit) {
            state = await CreatureSyncTools.GetBodyStateAsync(context, entityId);
            if (BodyStatesNear(expected, state)) {
                return state;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException(
            $"{context.RoleName} 实体 {entityId} 身体状态未接近服务端。服务端={expected}，最后本地={state}。");
    }

    private static bool BodyStatesNear(BodySyncState expected, BodySyncState actual) =>
        expected.Exists
        && actual.Exists
        && expected.Id == actual.Id
        && Vector3Dto.DistanceSquared(expected.Position, actual.Position) <= 1.0f
        && Math.Abs(expected.Velocity.X - actual.Velocity.X) <= 0.5f
        && Math.Abs(expected.Velocity.Y - actual.Velocity.Y) <= 0.5f
        && Math.Abs(expected.Velocity.Z - actual.Velocity.Z) <= 0.5f;

    private static async Task<VehicleState> GetVehicleStateAsync(GameContext context, int entityId) {
        object? result = await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("WonderfulEra/GetVehicleStateCode.cs").Replace("__ENTITY_ID__", entityId.ToString(CultureInfo.InvariantCulture)));
        return ParseVehicleState(result as string ?? "missing");
    }

    private static float VehicleHorizontalSpeed(Vector3Dto velocity) =>
        MathF.Sqrt(velocity.X * velocity.X + velocity.Z * velocity.Z);

    private static VehicleState ParseVehicleState(string value) {
        if (string.IsNullOrWhiteSpace(value)
            || value.Equals("missing", StringComparison.OrdinalIgnoreCase)
            || value.Equals("no-body", StringComparison.OrdinalIgnoreCase)) {
            return new VehicleState(false, default, default, 0f, 0f, false, -1f, -1, false, -1, value);
        }

        Dictionary<string, string> values = value
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => p[1], StringComparer.OrdinalIgnoreCase);

        return new VehicleState(
            true,
            ParseVector3(values, "pos"),
            ParseVector3(values, "vel"),
            ParseFloat(values, "move"),
            ParseFloat(values, "turn"),
            ParseInt(values, "run") == 1,
            ParseFloat(values, "energy"),
            ParseInt(values, "fuel"),
            ParseInt(values, "standing") == 1,
            ParseInt(values, "slots"),
            value);
    }

    private static VehicleTemplateInfo ParseVehicleTemplateInfo(string value) {
        if (string.IsNullOrWhiteSpace(value) || value.StartsWith("create-failed", StringComparison.OrdinalIgnoreCase)) {
            return new VehicleTemplateInfo(false, false, false, false, false, false, false, false, false, -1, value);
        }

        Dictionary<string, string> values = value
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => p[1], StringComparer.OrdinalIgnoreCase);

        return new VehicleTemplateInfo(
            true,
            ParseInt(values, "vehicle") == 1,
            ParseInt(values, "vehicleInventory") == 1,
            ParseInt(values, "carInventory") == 1,
            ParseInt(values, "blockEntity") == 1,
            ParseInt(values, "mount") == 1,
            ParseInt(values, "creature") == 1,
            ParseInt(values, "damage") == 1,
            ParseInt(values, "onFire") == 1,
            ParseInt(values, "slots"),
            value);
    }

    private static Vector3Dto ParseVector3(Dictionary<string, string> values, string key) {
        string[] parts = values.GetValueOrDefault(key, string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 3
            && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
            && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
            && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)) {
            return new Vector3Dto(x, y, z);
        }

        return default;
    }

    private static float ParseFloat(Dictionary<string, string> values, string key) =>
        values.TryGetValue(key, out string? value)
        && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result)
            ? result
            : 0f;

    private static int ParseInt(Dictionary<string, string> values, string key) =>
        values.TryGetValue(key, out string? value)
        && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
            ? result
            : -1;

    private static async Task<int> PlaceRefrigeratorAsync(GameContext context, float offsetX, float offsetY, float offsetZ) {
        string script = GameTools.ReadScript("WonderfulEra/PlaceRefrigeratorCode.cs")
            .Replace("__OFFSET_X__", offsetX.ToString(CultureInfo.InvariantCulture))
            .Replace("__OFFSET_Y__", offsetY.ToString(CultureInfo.InvariantCulture))
            .Replace("__OFFSET_Z__", offsetZ.ToString(CultureInfo.InvariantCulture));
        object? result = await context.PushCommandAsync("FUNC " + script);
        return result is int id ? id : Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private static async Task<RefrigeratorState> WaitForRefrigeratorSlottedAsync(GameContext context, int entityId) {
        RefrigeratorState state = default;
        for (int i = 0; i < 100; i++) {
            state = await GetRefrigeratorStateAsync(context, entityId);
            if (state.Exists && state.Slot0Count > 0) {
                return state;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"{context.RoleName} 冰箱 {entityId} 库存未就位。最后状态={state}。");
    }

    private static async Task<RefrigeratorState> GetRefrigeratorStateAsync(GameContext context, int entityId) {
        object? result = await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("WonderfulEra/GetRefrigeratorStateCode.cs").Replace("__ENTITY_ID__", entityId.ToString(CultureInfo.InvariantCulture)));
        return ParseRefrigeratorState(result as string ?? "missing");
    }

    private static RefrigeratorState ParseRefrigeratorState(string value) {
        if (string.IsNullOrWhiteSpace(value) || value.Equals("missing", StringComparison.OrdinalIgnoreCase)) {
            return new RefrigeratorState(false, -1, -1, -1, false, false, value);
        }

        string[] parts = value.Split('|');
        if (parts.Length != 5) {
            throw new FormatException($"无效冰箱状态: \"{value}\".");
        }

        return new RefrigeratorState(
            true,
            int.Parse(parts[0], CultureInfo.InvariantCulture),
            int.Parse(parts[1], CultureInfo.InvariantCulture),
            int.Parse(parts[2], CultureInfo.InvariantCulture),
            parts[3] == "1",
            parts[4] == "1",
            value);
    }

    private static async Task<DietState> GetPlayerDietStateAsync(GameContext context, int playerIndex) {
        object? result = await context.PushCommandAsync(
            "FUNC " + GameTools.ReadScript("WonderfulEra/GetPlayerDietStateCode.cs").Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture)));
        return ParseDietState(result as string ?? string.Empty);
    }

    private static async Task<DietState> WaitForServerAteAsync(GameContext context, int playerIndex, DietState before) {
        DietState state = default;
        for (int i = 0; i < 120; i++) {
            state = await GetPlayerDietStateAsync(context, playerIndex);
            if (state.Exists && state.Food - before.Food >= 0.2f) {
                return state;
            }

            await Task.Delay(100);
        }

        return state;
    }

    private static async Task<DietState> WaitForClientDietConvergedAsync(GameContext context, int playerIndex, DietState expected) {
        DietState state = default;
        for (int i = 0; i < 200; i++) {
            state = await GetPlayerDietStateAsync(context, playerIndex);
            if (state.Exists && Math.Abs(state.Food - expected.Food) <= VitalStatsSyncEpsilon) {
                return state;
            }

            await Task.Delay(100);
        }

        return state;
    }

    private static DietState ParseDietState(string value) {
        if (string.IsNullOrWhiteSpace(value) || value.Equals("missing", StringComparison.OrdinalIgnoreCase)) {
            return new DietState(false, 0f, 0f, 0f, 0f, 0f, -1f, false, 0f, value);
        }

        string[] parts = value.Split(',');
        if (parts.Length != 8) {
            throw new FormatException($"无效饮食状态: \"{value}\".");
        }

        return new DietState(
            true,
            float.Parse(parts[0], CultureInfo.InvariantCulture),
            float.Parse(parts[1], CultureInfo.InvariantCulture),
            float.Parse(parts[2], CultureInfo.InvariantCulture),
            float.Parse(parts[3], CultureInfo.InvariantCulture),
            float.Parse(parts[4], CultureInfo.InvariantCulture),
            float.Parse(parts[5], CultureInfo.InvariantCulture),
            parts[6] == "1",
            float.Parse(parts[7], CultureInfo.InvariantCulture),
            value);
    }

    private readonly record struct VehicleTemplateInfo(
        bool Created,
        bool HasVehicle,
        bool HasVehicleInventory,
        bool HasCarInventory,
        bool HasBlockEntity,
        bool HasMount,
        bool HasCreature,
        bool HasDamage,
        bool HasOnFire,
        int SlotsCount,
        string Raw);

    private readonly record struct VehicleState(
        bool Exists,
        Vector3Dto Position,
        Vector3Dto Velocity,
        float MoveOrder,
        float TurnOrder,
        bool Running,
        float Energy,
        int FuelCount,
        bool Standing,
        int SlotsCount,
        string Raw) {
        public override string ToString() =>
            string.Create(CultureInfo.InvariantCulture,
                $"(pos={Position.X:R},{Position.Y:R},{Position.Z:R};vel={Velocity.X:R},{Velocity.Y:R},{Velocity.Z:R};move={MoveOrder:R};turn={TurnOrder:R};run={Running};energy={Energy:R};fuel={FuelCount};standing={Standing};Raw='{Raw}')");
    }

    private readonly record struct RefrigeratorState(
        bool Exists,
        int SlotsCount,
        int Slot0Contents,
        int Slot0Count,
        bool PowerOn,
        bool MPowerOn,
        string Raw) {
        public string Inventory =>
            string.Create(CultureInfo.InvariantCulture, $"Slots={SlotsCount},slot0={Slot0Contents}:{Slot0Count}");

        public override string ToString() =>
            string.Create(CultureInfo.InvariantCulture,
                $"(Exists={Exists},Slots={SlotsCount},slot0={Slot0Contents}:{Slot0Count},PowerOn={PowerOn},m_powerOn={MPowerOn},Raw='{Raw}')");
    }

    private readonly record struct DietState(
        bool Exists,
        float Food,
        float Stamina,
        float Sleep,
        float Wetness,
        float Temperature,
        float Thirsty,
        bool Sick,
        float SicknessDuration,
        string Raw) {
        public override string ToString() =>
            string.Create(CultureInfo.InvariantCulture,
                $"(Food={Food:R},Stamina={Stamina:R},Sleep={Sleep:R},Wetness={Wetness:R},Temp={Temperature:R},Thirsty={Thirsty:R},Sick={Sick}({SicknessDuration:R}s),Raw='{Raw}')");
    }
}
