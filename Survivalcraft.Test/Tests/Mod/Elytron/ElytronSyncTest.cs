using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Xunit;

namespace Survivalcraft.Test;

[TestCaseOrderer("Survivalcraft.Test.PriorityOrderer", "Survivalcraft.Test")]
public sealed class ElytronSyncTest : IClassFixture<ElytronGameFixture>, IDisposable
{
    private readonly ElytronGameFixture fixture;
    private GameContext server => fixture.Server;
    private GameContext client => fixture.Client;

    public ElytronSyncTest(ElytronGameFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    [TestOrder(Priority = 1)]
    public async Task ElytronModLoadsAndInstallsBlocksOnBothProcesses()
    {
        await ElytronSyncTools.EnsureWorldAndClientAsync(server, client);
        int playerIndex = await ElytronSyncTools.ResolvePlayerIndexAsync(client);
        await PlayerSyncTools.WaitForPlayerEntityAsync(server, playerIndex);

        string serverState = await ElytronSyncTools.GetInstallStateAsync(server, playerIndex);
        string clientState = await ElytronSyncTools.GetInstallStateAsync(client, playerIndex);

        List<string> failures = new();
        AssertInstallState("服务端", serverState, failures);
        AssertInstallState("客户端", clientState, failures);
        if (serverState != clientState)
        {
            failures.Add($"两端安装信息不一致: server={serverState}; client={clientState}");
        }
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    [TestOrder(Priority = 2)]
    public async Task WearingElytraSynchronizesClothingAndAccessoryBetweenServerAndClient()
    {
        await ElytronSyncTools.EnsureWorldAndClientAsync(server, client);
        int playerIndex = await ElytronSyncTools.ResolvePlayerIndexAsync(client);
        await PlayerSyncTools.WaitForPlayerEntityAsync(server, playerIndex);

        List<string> failures = new();

        string baselineServer = await ElytronSyncTools.GetEquipmentFingerprintAsync(server, playerIndex);
        string baselineClient = await ElytronSyncTools.GetEquipmentFingerprintAsync(client, playerIndex);

        string equip = await ElytronSyncTools.EquipElytraAsync(server, playerIndex);
        if (!equip.StartsWith("ok:", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add($"服务端装备鞘翅失败: {equip}");
        }
        else
        {
            await Task.Delay(700); // 等待 ClothingSyncPacket 送达客户端并重新触发 accessory 钩子

            string serverState = await ElytronSyncTools.GetEquipmentFingerprintAsync(server, playerIndex);
            string clientState = await ElytronSyncTools.GetEquipmentFingerprintAsync(client, playerIndex);

            if (!string.Equals(serverState, clientState, StringComparison.Ordinal))
            {
                failures.Add($"穿上鞘翅后两端不一致: server={serverState}; client={clientState}");
            }
        }
        if (baselineServer != baselineClient)
        {
            failures.Add($"装备前基线不一致: server={baselineServer}; client={baselineClient}");
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    [TestOrder(Priority = 3)]
    public async Task GlidingActivationThroughCompatAdapterAndStateVelocitySynchronization()
    {
        await ElytronSyncTools.EnsureWorldAndClientAsync(server, client);
        int playerIndex = await ElytronSyncTools.ResolvePlayerIndexAsync(client);
        await PlayerSyncTools.WaitForPlayerEntityAsync(server, playerIndex);

        int mode = await ElytronSyncTools.GetWorldModeAsync(server);
        Assert.True(mode != 0, $"服务端世界不是非创造模式（GameMode={mode}），滑翔被模组强制禁用。");

        await ElytronSyncTools.EquipElytraAsync(server, playerIndex);
        await Task.Delay(500);
        string teleport = await ElytronSyncTools.TeleportPlayerUpAsync(server, playerIndex, 45f);
        await Task.Delay(350);

        string inject = await ElytronSyncTools.InjectDoubleJumpAsync(client, playerIndex);
        await Task.Delay(600); // 服务端双击处理 + 身体同步包

        List<string> failures = new();
        for (int i = 1; i <= 2; i++)
        {
            string serverGlide = await ElytronSyncTools.GetGlideFingerprintAsync(server, playerIndex);
            string clientGlide = await ElytronSyncTools.GetGlideFingerprintAsync(client, playerIndex);

            Dictionary<string, string> sv = ElytronSyncTools.ParseKeyValuePairs(serverGlide);
            Dictionary<string, string> cv = ElytronSyncTools.ParseKeyValuePairs(clientGlide);

            string serverActive = sv.GetValueOrDefault("active", "?");
            string clientActive = cv.GetValueOrDefault("active", "?");

            if (!string.Equals(serverActive, "True", StringComparison.OrdinalIgnoreCase))
            {
                failures.Add($"第{i}次取样: CompatNet 双击跳跃未能让服务端激活滑翔 (server active={serverActive}), inject={inject}, teleport={teleport}, serverFp={serverGlide}, clientFp={clientGlide}");
                break;
            }

            bool stateMatch = string.Equals(serverActive, clientActive, StringComparison.OrdinalIgnoreCase);
            if (!stateMatch)
            {
                failures.Add($"第{i}次取样: 滑翔开关不同步 server={serverActive}, client={clientActive}, serverFp={serverGlide}, clientFp={clientGlide}");
            }

            float poseDiff = ParseFloat(cv, "pose") - ParseFloat(sv, "pose");
            if (Math.Abs(poseDiff) > 0.2f)
            {
                failures.Add($"第{i}次取样: m_glidePoseFactor 不同步 diff={poseDiff:R}, serverFp={serverGlide}, clientFp={clientGlide}");
            }

            bool velNear = ElytronSyncTools.VelocitiesNear(
                ParseFloat(sv, "vx"), ParseFloat(sv, "vy"), ParseFloat(sv, "vz"),
                ParseFloat(cv, "vx"), ParseFloat(cv, "vy"), ParseFloat(cv, "vz"), 4f);
            if (!velNear)
            {
                failures.Add($"第{i}次取样: 滑翔速度不同步 serverFp={serverGlide}, clientFp={clientGlide}");
            }

            if (i == 1)
            {
                await Task.Delay(350);
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    [TestOrder(Priority = 4)]
    public async Task FireworksBoostVelocitySynchronizesBetweenServerAndClient()
    {
        await ElytronSyncTools.EnsureWorldAndClientAsync(server, client);
        int playerIndex = await ElytronSyncTools.ResolvePlayerIndexAsync(client);
        await PlayerSyncTools.WaitForPlayerEntityAsync(server, playerIndex);

        string seed = await ElytronSyncTools.SeedFireworkInActiveSlotAsync(server, playerIndex, 5);
        await Task.Delay(450); // 服务端 ComponentGliding.Update 记录烟花数量 token
        string decrease = await ElytronSyncTools.DecreaseFireworkCountAsync(server, playerIndex, 1);
        await Task.Delay(250); // 服务端升空 + 身体同步包初达

        List<string> failures = new();
        bool clientEverRose = false;
        bool serverEverFast = false;
        for (int i = 1; i <= 2; i++)
        {
            string serverFp = await ElytronSyncTools.GetGlideFingerprintAsync(server, playerIndex);
            string clientFp = await ElytronSyncTools.GetGlideFingerprintAsync(client, playerIndex);

            Dictionary<string, string> sv = ElytronSyncTools.ParseKeyValuePairs(serverFp);
            Dictionary<string, string> cv = ElytronSyncTools.ParseKeyValuePairs(clientFp);

            float serverVy = ParseFloat(sv, "vy");
            float clientVy = ParseFloat(cv, "vy");
            if (serverVy > 2f)
            {
                serverEverFast = true;
            }
            if (clientVy > 2f)
            {
                clientEverRose = true;
            }

            bool vyNear = Math.Abs(serverVy - clientVy) <= 2.5f;
            bool velNear = ElytronSyncTools.VelocitiesNear(
                ParseFloat(sv, "vx"), serverVy, ParseFloat(sv, "vz"),
                ParseFloat(cv, "vx"), clientVy, ParseFloat(cv, "vz"), 4f);
            if (!vyNear || !velNear)
            {
                failures.Add($"第{i}次取样: 烟花升空速度不同步 serverFp={serverFp}, clientFp={clientFp}");
            }

            if (i == 1)
            {
                await Task.Delay(400);
            }
        }

        if (!serverEverFast)
        {
            failures.Add($"服务端全程未出现 vy>2 的升空，触发可能失败: seed={seed}, decrease={decrease}");
        }
        if (!clientEverRose)
        {
            failures.Add("客户端玩家在整个观测窗口内 vy 从未超过 2，烟花升空完全没有同步到客户端");
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    public void Dispose()
    {
        fixture.ThrowIfAnyContextHasError();
    }

    private static void AssertInstallState(string role, string state, List<string> failures)
    {
        if (!state.Contains("elytron=True", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add($"{role} 未加载 Elytron 模组: {state}");
        }
        if (!state.Contains("compatnet=True", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add($"{role} 未加载 CompatNet: {state}");
        }
        if (!state.Contains("gliding=Elytron.ComponentGliding", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add($"{role} 缺少 ComponentGliding 类型: {state}");
        }
        if (!state.Contains("blockType=Elytron.ElytraClothingBlock", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add($"{role} 方块 322 不是 ElytraClothingBlock: {state}");
        }
        if (!state.Contains("components=gliding=true", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add($"{role} 玩家实体上缺少 ComponentGliding: {state}");
        }
    }

    private static float ParseFloat(Dictionary<string, string> values, string key)
    {
        return values.TryGetValue(key, out string? value)
            && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result)
                ? result
                : float.NaN;
    }
}