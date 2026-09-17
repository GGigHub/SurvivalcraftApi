using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Engine;
using Xunit;
using Xunit.Abstractions;

namespace Survivalcraft.Test;

[TestCaseOrderer("Survivalcraft.Test.PriorityOrderer", "Survivalcraft.Test")]
public sealed class SubnauticaSyncTest : IClassFixture<SubnauticaGameFixture>, IDisposable {
    private readonly SubnauticaGameFixture fixture;
    private readonly ITestOutputHelper output;

    private GameContext server => fixture.Server;

    private GameContext client => fixture.Client;

    public SubnauticaSyncTest(SubnauticaGameFixture fixture, ITestOutputHelper output) {
        this.fixture = fixture;
        this.output = output;
    }

    [Fact]
    [TestOrder(Priority = 1)]
    public async Task WorldCreatedAndPreJoinSoulTorchPlaced() {
        await EnsureWorldAndClientAsync();

        Point3 origin = fixture.State.PlayerCell!.Value;
        string placement = await SubnauticaSyncTools.PlaceSoulTorchAsync(server, origin);
        Assert.Contains("torch=", placement, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("torch=-1", placement, StringComparison.OrdinalIgnoreCase);

        Point3 torchCell = new(origin.X + 1, origin.Y + 1, origin.Z + 1);
        fixture.State.PreJoinTorchCell = torchCell;

        string serverRegistry = await SubnauticaSyncTools.GetSoulTorchRegistryAsync(
            server,
            torchCell,
            torchCell.X + 0.5,
            torchCell.Y + 0.5,
            torchCell.Z + 0.5);
        Dictionary<string, string> serverValues = SubnauticaSyncTools.ParseFingerprint(serverRegistry);
        Assert.True(int.Parse(serverValues["count"], CultureInfo.InvariantCulture) >= 1, "服务端 SoulTorchManager 里没有雷达。" + serverRegistry);
        Assert.Equal("True", serverValues["near"]);
    }

    [Fact]
    [TestOrder(Priority = 2)]
    public async Task PostJoinLivePlacementSynchronizesSoulTorchCoverage() {
        await EnsureWorldAndClientAsync();
        Point3 origin = fixture.State.PlayerCell!.Value;

        // 客户端已在线且该区域 chunk 已加载：火把经实时地形变更包(ChangeCell)到达客户端，
        // 客户端 OnBlockAdded → SoulTorchManager 登记雷达 → 覆盖范围两端一致。
        // 放在高空、且靠近出生点保证 chunk 已加载（未加载的 chunk 里 ChangeCell 会被静默忽略）。
        Point3 anchor = new(origin.X + 15, origin.Y + 40, origin.Z);
        string placement = await SubnauticaSyncTools.PlaceSoulTorchAsync(server, anchor);
        Assert.DoesNotContain("torch=-1", placement, StringComparison.OrdinalIgnoreCase);

        Point3 torchCell = new(anchor.X + 1, anchor.Y + 1, anchor.Z + 1);
        int serverTorchValue = await SubnauticaSyncTools.GetCellValueAsync(server, torchCell);
        Assert.NotEqual(0, serverTorchValue);
        int clientCell = await SubnauticaSyncTools.WaitForCellValueAsync(client, torchCell, serverTorchValue);
        Assert.Equal(serverTorchValue, clientCell);

        string serverRegistry = await SubnauticaSyncTools.GetSoulTorchRegistryAsync(
            server,
            torchCell,
            torchCell.X + 0.5,
            torchCell.Y + 0.5,
            torchCell.Z + 0.5);
        string clientRegistry = await SubnauticaSyncTools.GetSoulTorchRegistryAsync(
            client,
            torchCell,
            torchCell.X + 0.5,
            torchCell.Y + 0.5,
            torchCell.Z + 0.5);
        Dictionary<string, string> serverValues = SubnauticaSyncTools.ParseFingerprint(serverRegistry);
        Dictionary<string, string> clientValues = SubnauticaSyncTools.ParseFingerprint(clientRegistry);

        output.WriteLine($"SoulTorch 地形格两端一致: server={serverTorchValue}; client={clientCell}");
        output.WriteLine($"SoulTorch 服务端注册表: {serverRegistry}");
        output.WriteLine($"SoulTorch 客户端注册表: {clientRegistry}");

        Assert.True(serverValues.GetValueOrDefault("near") == "True", "服务端没有把新放置的火把登记进 SoulTorchManager：" + serverRegistry);
        Assert.True(
            clientValues.GetValueOrDefault("near") == "True",
            "客户端未识别到新放置火把的覆盖（实时放置路径未同步）：" + clientRegistry);
    }

    [Fact]
    [TestOrder(Priority = 3)]
    public async Task PreJoinSoulTorchLeavesStaleRadarOnServer() {
        await EnsureWorldAndClientAsync();
        Assert.NotNull(fixture.State.PreJoinTorchCell);
        Point3 torchCell = fixture.State.PreJoinTorchCell!.Value;

        // 客户端连接后，服务端该区域的 chunk 被重新生成，火把格被抹成空气，
        // 但服务端 SoulTorchManager 里残留着雷达；客户端注册表始终为空。
        int serverCell = await SubnauticaSyncTools.GetCellValueAsync(server, torchCell);
        string serverRegistry = await SubnauticaSyncTools.GetSoulTorchRegistryAsync(
            server,
            torchCell,
            torchCell.X + 0.5,
            torchCell.Y + 0.5,
            torchCell.Z + 0.5);
        string clientRegistry = await SubnauticaSyncTools.GetSoulTorchRegistryAsync(
            client,
            torchCell,
            torchCell.X + 0.5,
            torchCell.Y + 0.5,
            torchCell.Z + 0.5);

        output.WriteLine($"PreJoin 火把格服务端当前格值: {serverCell} (0=已被抹掉)");
        output.WriteLine($"PreJoin 服务端注册表: {serverRegistry}");
        output.WriteLine($"PreJoin 客户端注册表: {clientRegistry}");

        SubnauticaSyncTools.AssertStateDiffers("pre-join 灵魂火把覆盖(SoulTorchManager)", serverRegistry, clientRegistry);
    }

    [Fact]
    [TestOrder(Priority = 4)]
    public async Task ModBlockPaletteTerrainSynchronizes() {
        await EnsureWorldAndClientAsync();
        Point3 origin = fixture.State.PlayerCell!.Value;

        string placement = await SubnauticaSyncTools.PlaceModBlockPaletteAsync(server, origin);
        Assert.DoesNotContain("=-1", placement, StringComparison.OrdinalIgnoreCase);

        // 方块调色板被放在 origin.Y+30 的高空（见 PlaceModBlockPaletteAsync）。
        Point3 regionOrigin = new(origin.X + 10, origin.Y + 30, origin.Z);
        string serverFingerprint = string.Empty;
        string clientFingerprint = string.Empty;
        for (int i = 0; i < 150; i++) {
            serverFingerprint = await SubnauticaSyncTools.GetTerrainFingerprintAsync(server, regionOrigin, 0, 0, 0, 6, 0, 0);
            clientFingerprint = await SubnauticaSyncTools.GetTerrainFingerprintAsync(client, regionOrigin, 0, 0, 0, 6, 0, 0);
            if (string.Equals(serverFingerprint, clientFingerprint, StringComparison.Ordinal)) {
                break;
            }

            await Task.Delay(100);
        }

        output.WriteLine($"mod 方块调色板放置结果: {placement}");
        output.WriteLine($"mod 方块地形 服务端指纹: {serverFingerprint}");
        output.WriteLine($"mod 方块地形 客户端指纹: {clientFingerprint}");
        SubnauticaSyncTools.AssertStateEquals("mod 方块地形(服务端放置 7 种 mod 方块)", serverFingerprint, clientFingerprint);
    }

    [Fact]
    [TestOrder(Priority = 5)]
    public async Task MermaidAffectionSynchronizes() {
        await EnsureWorldAndClientAsync();
        Point3 origin = fixture.State.PlayerCell!.Value;

        int mermaidId = await SubnauticaSyncTools.SpawnCreatureAsync(
            server,
            "FemaleHumanFish",
            new Vector3(origin.X + 14f, origin.Y + 0.5f, origin.Z + 14f));
        Assert.True(mermaidId > 0, "服务端未能生成 FemaleHumanFish。");
        await SubnauticaSyncTools.WaitForEntityAsync(client, mermaidId);

        string feed = await SubnauticaSyncTools.FeedMermaidAsync(server, mermaidId);
        Assert.StartsWith("ok=True", feed, StringComparison.OrdinalIgnoreCase);

        string serverState = string.Empty;
        string clientState = string.Empty;
        for (int i = 0; i < 90; i++) {
            await Task.Delay(150);
            serverState = await SubnauticaSyncTools.GetMermaidStateAsync(server, mermaidId);
            clientState = await SubnauticaSyncTools.GetMermaidStateAsync(client, mermaidId);
            if (string.Equals(serverState, clientState, StringComparison.Ordinal)) {
                break;
            }
        }

        output.WriteLine($"人鱼喂食结果: {feed}");
        output.WriteLine($"人鱼好感 服务端: {serverState}");
        output.WriteLine($"人鱼好感 客户端: {clientState}");

        // CompatNet 的服务器端快照发布钩子 "SubsystemUpdate" 未注册，
        // OnServerUpdate 从未执行 → 人鱼好感状态没有任何推送路径到客户端。
        SubnauticaSyncTools.AssertStateDiffers("人鱼好感(喂食后 AffectionPoints/CurrentState)", serverState, clientState);
    }

    [Fact]
    [TestOrder(Priority = 6)]
    public async Task EelShockStateDoesNotSynchronize() {
        await EnsureWorldAndClientAsync();
        Point3 origin = fixture.State.PlayerCell!.Value;

        int eelId = await SubnauticaSyncTools.SpawnCreatureAsync(
            server,
            "Eel",
            new Vector3(origin.X + 16f, origin.Y - 2f, origin.Z + 16f));
        Assert.True(eelId > 0, "服务端未能生成 Eel。");
        await SubnauticaSyncTools.WaitForEntityAsync(client, eelId);

        string force = await SubnauticaSyncTools.ForceEelShockStateAsync(server, eelId);
        Assert.Equal("forced", force);

        await Task.Delay(1200);

        string serverState = await SubnauticaSyncTools.GetEelShockStateAsync(server, eelId);
        string clientState = await SubnauticaSyncTools.GetEelShockStateAsync(client, eelId);

        output.WriteLine($"电鳗状态 服务端: {serverState}");
        output.WriteLine($"电鳗状态 客户端: {clientState}");
        SubnauticaSyncTools.AssertStateDiffers("电鳗蓄力/放电状态(ComponentEelShockAttack.IsCharging/IsShocking)", serverState, clientState);
    }

    [Fact]
    [TestOrder(Priority = 7)]
    public async Task AchievementsDoNotSynchronize() {
        await EnsureWorldAndClientAsync();
        int playerIndex = await PlayerSyncTools.WaitForClientPlayerIndexAsync(client);
        Assert.True(playerIndex >= 0);

        int playerEntityId = await SubnauticaSyncTools.GetPlayerEntityIdAsync(server, playerIndex);
        Assert.True(playerEntityId > 0);

        string result = await SubnauticaSyncTools.UnlockAchievementAsync(server, playerEntityId);
        Assert.Equal("reported", result);

        await Task.Delay(1500);

        string serverState = await SubnauticaSyncTools.GetAchievementsStateAsync(server, playerEntityId);
        string clientState = await SubnauticaSyncTools.GetAchievementsStateAsync(client, playerEntityId);

        output.WriteLine($"成就(玩家 {playerEntityId}) 服务端: {serverState}");
        output.WriteLine($"成就(玩家 {playerEntityId}) 客户端: {clientState}");
        SubnauticaSyncTools.AssertStateDiffers("成就(AchievementManager 按玩家 EntityId 索引)", serverState, clientState);
    }

    [Fact]
    [TestOrder(Priority = 8)]
    public async Task KrakenEncounterDoesNotSynchronize() {
        await EnsureWorldAndClientAsync();
        int playerIndex = await PlayerSyncTools.WaitForClientPlayerIndexAsync(client);
        Assert.True(playerIndex >= 0);

        Point3 origin = fixture.State.PlayerCell!.Value;
        Point3 trigger = new(origin.X + 80, origin.Y + 12, origin.Z + 80);
        string triggerResult = await SubnauticaSyncTools.TriggerKrakenEncounterAsync(server, trigger, playerIndex);
        Assert.Contains("handled=True", triggerResult, StringComparison.OrdinalIgnoreCase);

        await Task.Delay(500);

        string serverState = await SubnauticaSyncTools.GetKrakenEncounterStateAsync(server);
        string clientState = await SubnauticaSyncTools.GetKrakenEncounterStateAsync(client);

        output.WriteLine($"Kraken 遭遇战 服务端: {serverState}");
        output.WriteLine($"Kraken 遭遇战 客户端: {clientState}");
        SubnauticaSyncTools.AssertStateDiffers("Kraken 遭遇战(KrakenEncounterManager Phase/阶段计时)", serverState, clientState);
    }

    public void Dispose() {
        fixture.ThrowIfAnyContextHasError();
    }

    private async Task EnsureWorldAndClientAsync() {
        if (!(await SubnauticaSyncTools.IsProjectReadyAsync(server))) {
            await PlayerSyncTools.CreateCreativeTestWorldAsync(server, "subnautica-sync");
            fixture.State.PlayerCell = await SubnauticaSyncTools.GetPlayerCellAsync(server);
            string serverLoad = await SubnauticaSyncTools.GetModLoadStateAsync(server);
            Assert.Contains("subnautica=True", serverLoad, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("compatnet=True", serverLoad, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("loader=True", serverLoad, StringComparison.OrdinalIgnoreCase);
        }

        if (!(await SubnauticaSyncTools.IsProjectReadyAsync(client))) {
            await GameTools.ConnectTestServer(client, server.GamePort);
            string clientLoad = await SubnauticaSyncTools.GetModLoadStateAsync(client);
            Assert.Contains("subnautica=True", clientLoad, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("compatnet=True", clientLoad, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("loader=True", clientLoad, StringComparison.OrdinalIgnoreCase);
        }

        await PlayerSyncTools.WaitForPlayerEntityAsync(playerIndex: await PlayerSyncTools.WaitForClientPlayerIndexAsync(client), context: server);
    }
}