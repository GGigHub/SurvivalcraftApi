using System;
using System.Globalization;
using System.Threading.Tasks;
using Xunit;

namespace Survivalcraft.Test;

[TestCaseOrderer("Survivalcraft.Test.PriorityOrderer", "Survivalcraft.Test")]
public sealed class GhoulSyncTest : IClassFixture<GhoulGameFixture>, IDisposable
{
	private readonly GhoulGameFixture fixture;

	private GameContext server => fixture.Server;

	private GameContext client => fixture.Client;

	public GhoulSyncTest(GhoulGameFixture fixture)
	{
		this.fixture = fixture;
	}

	[Fact]
	[TestOrder(Priority = 1)]
	public async Task GhoulModLoadsAndCreatesPlayerComponentsOnBothEnds()
	{
		await EnsureWorldAndClientAsync();
		string serverState = await GhoulSyncTools.ReadGhoulWorldStateAsync(server);
		string clientState = await GhoulSyncTools.ReadGhoulWorldStateAsync(client);
		Assert.Contains("modLoaded=True", serverState, StringComparison.Ordinal);
		Assert.Contains("modLoaded=True", clientState, StringComparison.Ordinal);
		Assert.Contains("sagSubsystem=True", serverState, StringComparison.Ordinal);
		Assert.Contains("sagSubsystem=True", clientState, StringComparison.Ordinal);
		Assert.Contains("tartareositySubsystem=True", serverState, StringComparison.Ordinal);
		Assert.Contains("tartareositySubsystem=True", clientState, StringComparison.Ordinal);
		Assert.Contains("artifactBehaviorSubsystem=True", serverState, StringComparison.Ordinal);
		Assert.Contains("artifactBehaviorSubsystem=True", clientState, StringComparison.Ordinal);
	}

	[Fact]
	[TestOrder(Priority = 2)]
	public async Task SAGConfigurationDifficultyIsNotSynchronizedFromClient()
	{
		await EnsureWorldAndClientAsync();
		string serverBefore = await GhoulSyncTools.ReadSAGSubsystemStateAsync(server);
		string clientBefore = await GhoulSyncTools.ReadSAGSubsystemStateAsync(client);

		// 模拟客户端玩家在 OptionDialog 里选了 Hard
		await GhoulSyncTools.SetSAGDifficultyAsync(client, 4);
		await Task.Delay(600);
		string serverAfter = await GhoulSyncTools.ReadSAGSubsystemStateAsync(server);
		string clientAfter = await GhoulSyncTools.ReadSAGSubsystemStateAsync(client);

		string evidence = string.Concat(
			"服务端 before=", serverBefore, "\n",
			"客户端 before=", clientBefore, "\n",
			"服务端 after=", serverAfter, "\n",
			"客户端 after=", clientAfter);
		Assert.True(string.Equals(serverAfter, clientAfter, StringComparison.Ordinal),
			"SubsystemSAG.Difficulty 在客户端修改后未同步到服务端（不同步）:\n" + evidence);
	}

	[Fact]
	[TestOrder(Priority = 3)]
	public async Task BloodMoonSpawnFromClientIsNotSynchronizedToServer()
	{
		await EnsureWorldAndClientAsync();
		int playerIndex = await PlayerSyncTools.WaitForClientPlayerIndexAsync(client);
		string clientPos = await GhoulSyncTools.ReadPlayerPositionAsync(client, playerIndex);
		string[] pos = clientPos.Split(',');
		float cx = float.Parse(pos[0], CultureInfo.InvariantCulture);
		float cy = float.Parse(pos[1], CultureInfo.InvariantCulture);
		float cz = float.Parse(pos[2], CultureInfo.InvariantCulture);
		string centerX = cx.ToString("R", CultureInfo.InvariantCulture);
		string centerY = cy.ToString("R", CultureInfo.InvariantCulture);
		string centerZ = cz.ToString("R", CultureInfo.InvariantCulture);

		await ClearSAGhoulsAsync(server);
		await ClearSAGhoulsAsync(client);
		await Task.Delay(400);

		// 客户端本地刷怪（血月刷怪路径）
		string spawnResult = await GhoulSyncTools.SpawnSAGhoulAtPositionAsync(client, "SAGhoul_Weak_01", cx + 5, cy, cz, false);
		await Task.Delay(1200);
		string serverFp = await GhoulSyncTools.ReadSAGhoulCreaturesAsync(server, centerX, centerY, centerZ, 40f);
		string clientFp = await GhoulSyncTools.ReadSAGhoulCreaturesAsync(client, centerX, centerY, centerZ, 40f);

		string evidence = string.Concat(
			"spawnResult=", spawnResult, "\n",
			"服务端=(", serverFp, ")\n",
			"客户端=(", clientFp, ")");
		Assert.True(string.Equals(serverFp, clientFp, StringComparison.Ordinal),
			"客户端血月刷怪未同步到服务端（双端刷怪集合不一致）:\n" + evidence);
	}

	[Fact]
	[TestOrder(Priority = 4)]
	public async Task ServerSpawnedSAGhoulReachesClientWithSameComponentState()
	{
		await EnsureWorldAndClientAsync();
		int playerIndex = await PlayerSyncTools.WaitForClientPlayerIndexAsync(client);
		string clientPos = await GhoulSyncTools.ReadPlayerPositionAsync(client, playerIndex);
		string[] pos = clientPos.Split(',');
		float cx = float.Parse(pos[0], CultureInfo.InvariantCulture);
		float cy = float.Parse(pos[1], CultureInfo.InvariantCulture);
		float cz = float.Parse(pos[2], CultureInfo.InvariantCulture);
		string centerX = cx.ToString("R", CultureInfo.InvariantCulture);
		string centerY = cy.ToString("R", CultureInfo.InvariantCulture);
		string centerZ = cz.ToString("R", CultureInfo.InvariantCulture);

		await ClearSAGhoulsAsync(server);
		await ClearSAGhoulsAsync(client);
		await Task.Delay(400);

		// 服务端刷怪（服务端权威刷怪路径）
		string spawnResult = await GhoulSyncTools.SpawnSAGhoulAtPositionAsync(server, "SAGhoul_Weak_01", cx + 5, cy, cz, false);
		await WaitUntilContainsAsync(client, centerX, centerY, centerZ, "SAGhoul_Weak_01");
		string serverFp = await GhoulSyncTools.ReadSAGhoulCreaturesAsync(server, centerX, centerY, centerZ, 40f);
		string clientFp = await GhoulSyncTools.ReadSAGhoulCreaturesAsync(client, centerX, centerY, centerZ, 40f);

		string evidence = string.Concat(
			"spawnResult=", spawnResult, "\n",
			"服务端=(", serverFp, ")\n",
			"客户端=(", clientFp, ")");
		Assert.True(string.Equals(serverFp, clientFp, StringComparison.Ordinal),
			"服务端刷出的 SAGhoul 在客户端上实体/组件状态不一致:\n" + evidence);
	}

	[Fact]
	[TestOrder(Priority = 5)]
	public async Task PlayerSoulsValueIsNotSynchronizedFromClient()
	{
		await EnsureWorldAndClientAsync();
		int playerIndex = await PlayerSyncTools.WaitForClientPlayerIndexAsync(client);
		string serverBefore = await GhoulSyncTools.ReadPlayerSAGComponentStateAsync(server, playerIndex);
		string clientBefore = await GhoulSyncTools.ReadPlayerSAGComponentStateAsync(client, playerIndex);

		await GhoulSyncTools.SetPlayerSoulsValueAsync(client, playerIndex, 0.7f);
		await Task.Delay(600);
		string serverAfter = await GhoulSyncTools.ReadPlayerSAGComponentStateAsync(server, playerIndex);
		string clientAfter = await GhoulSyncTools.ReadPlayerSAGComponentStateAsync(client, playerIndex);

		string evidence = string.Concat(
			"服务端 before=", serverBefore, "\n",
			"客户端 before=", clientBefore, "\n",
			"服务端 after=", serverAfter, "\n",
			"客户端 after=", clientAfter);
		Assert.True(string.Equals(serverAfter, clientAfter, StringComparison.Ordinal),
			"玩家 ComponentSAGPlayer.SoulsValue 在客户端修改后未同步到服务端（不同步）:\n" + evidence);
	}

	[Fact]
	[TestOrder(Priority = 6)]
	public async Task ClothingArmorProtectionIsNotSynchronizedFromClient()
	{
		await EnsureWorldAndClientAsync();
		int playerIndex = await PlayerSyncTools.WaitForClientPlayerIndexAsync(client);
		string serverBefore = await GhoulSyncTools.ReadClothingMassStateAsync(server, playerIndex);
		string clientBefore = await GhoulSyncTools.ReadClothingMassStateAsync(client, playerIndex);

		// 让客户端穿上 SAG 护甲（触发 SetClothes 钩子计算 ArmorProtection）
		string equipResult = await EquipSAGArmorOnPlayerAsync(client, playerIndex);
		await Task.Delay(800);
		string serverAfter = await GhoulSyncTools.ReadClothingMassStateAsync(server, playerIndex);
		string clientAfter = await GhoulSyncTools.ReadClothingMassStateAsync(client, playerIndex);

		string evidence = string.Concat(
			"equipResult=", equipResult, "\n",
			"服务端 before=", serverBefore, "\n",
			"客户端 before=", clientBefore, "\n",
			"服务端 after=", serverAfter, "\n",
			"客户端 after=", clientAfter);
		Assert.True(string.Equals(serverAfter, clientAfter, StringComparison.Ordinal),
			"ComponentClothingMass.ArmorProtection 在客户端穿甲后未同步到服务端（不同步）:\n" + evidence);
	}

	[Fact]
	[TestOrder(Priority = 7)]
	public async Task SubsystemTartareosityBossStateIsNotSynchronizedFromClient()
	{
		await EnsureWorldAndClientAsync();
		string serverBefore = await GhoulSyncTools.ReadTartareosityStateAsync(server);
		string clientBefore = await GhoulSyncTools.ReadTartareosityStateAsync(client);

		// 在客户端安排 BOSS 生成（CorpsificeSpawnData）
		string setResult = await SetCorpsificeSpawnDataAsync(client, 123456.0);
		await Task.Delay(800);
		string serverAfter = await GhoulSyncTools.ReadTartareosityStateAsync(server);
		string clientAfter = await GhoulSyncTools.ReadTartareosityStateAsync(client);

		string evidence = string.Concat(
			"setResult=", setResult, "\n",
			"服务端 before=", serverBefore, "\n",
			"客户端 before=", clientBefore, "\n",
			"服务端 after=", serverAfter, "\n",
			"客户端 after=", clientAfter);
		Assert.True(string.Equals(serverAfter, clientAfter, StringComparison.Ordinal),
			"SubsystemTartareosity 的 BOSS 状态（GameTimeToSpawn/State）在客户端写入后未同步到服务端（不同步）:\n" + evidence);
	}

	public void Dispose()
	{
		fixture.ThrowIfAnyContextHasError();
	}

	private async Task EnsureWorldAndClientAsync()
	{
		if (!(await GhoulSyncTools.IsProjectReadyAsync(server)))
		{
			await PlayerSyncTools.CreateCreativeTestWorldAsync(server, "ghoul-sync");
		}
		if (!(await GhoulSyncTools.IsProjectReadyAsync(client)))
		{
			await GameTools.ConnectTestServer(client, server.GamePort);
		}
		await PlayerSyncTools.WaitForPlayerEntityAsync(playerIndex: await PlayerSyncTools.WaitForClientPlayerIndexAsync(client), context: server);
		// 直接走 FUNC 决定，避免 OptionDialog 阻塞 UI；两端统一为正常的难度 2
		await GhoulSyncTools.SetSAGDifficultyAsync(server, 2);
	}

	private static async Task ClearSAGhoulsAsync(GameContext context)
	{
		const string script = "FUNC var project = Game.GameManager.Project;\nif (project == null) return \"no-project\";\nforeach (var e in project.Entities.ToList()) {\n    if (e.FindComponent<SAGhoul.Components.ComponentSAGhouls>() != null && e.FindComponent<Game.ComponentPlayer>() == null) {\n        project.RemoveEntity(e, true);\n    }\n}\nreturn \"ok\";";
		await context.PushCommandAsync(script);
	}

	private static async Task WaitUntilContainsAsync(GameContext context, string centerX, string centerY, string centerZ, string expected)
	{
		for (int i = 0; i < 80; i++)
		{
			string fp = await GhoulSyncTools.ReadSAGhoulCreaturesAsync(context, centerX, centerY, centerZ, 40f);
			if (fp.Contains(expected, StringComparison.OrdinalIgnoreCase))
			{
				return;
			}
			await Task.Delay(100);
		}
		Assert.Fail(context.RoleName + " did not observe " + expected + ".");
	}

	private static async Task<string> EquipSAGArmorOnPlayerAsync(GameContext context, int playerIndex)
	{
		string script = "FUNC var project = Game.GameManager.Project;\nif (project == null) return \"no-project\";\nvar players = project.FindSubsystem<Game.SubsystemPlayers>();\nvar playerData = players.PlayersData.FirstOrDefault(p => p.PlayerIndex == __PLAYER_INDEX__);\nif (playerData == null || playerData.ComponentPlayer == null) return \"no-player\";\nvar clothing = playerData.ComponentPlayer.ComponentClothing;\nif (clothing == null) return \"no-clothing\";\nvar clothingMass = playerData.ComponentPlayer.Entity.FindComponent<SAGhoul.Components.ComponentClothingMass>();\nif (clothingMass == null) return \"no-clothing-mass\";\nint cIndex = Game.ClothingBlock.Index;\nclothing.SetClothes(Game.ClothingSlot.Head, new int[] { Game.Terrain.MakeBlockValue(cIndex, 0, Game.ClothingBlock.SetClothingIndex(0, 90)) });\nclothing.SetClothes(Game.ClothingSlot.Legs, new int[] { Game.Terrain.MakeBlockValue(cIndex, 0, Game.ClothingBlock.SetClothingIndex(0, 91)) });\nclothing.SetClothes(Game.ClothingSlot.Torso, new int[] { Game.Terrain.MakeBlockValue(cIndex, 0, Game.ClothingBlock.SetClothingIndex(0, 92)) });\nclothing.SetClothes(Game.ClothingSlot.Feet, new int[] { Game.Terrain.MakeBlockValue(cIndex, 0, Game.ClothingBlock.SetClothingIndex(0, 93)) });\nreturn \"equipped:\" + clothingMass.ArmorProtection.ToString(\"R\", System.Globalization.CultureInfo.InvariantCulture);".Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture));
		return (string)(await context.PushCommandAsync(script)) ?? string.Empty;
	}

	private static async Task<string> SetCorpsificeSpawnDataAsync(GameContext context, double gameTimeToSpawn)
	{
		string script = "FUNC var project = Game.GameManager.Project;\nif (project == null) return \"no-project\";\nvar tart = project.FindSubsystem<SAGhoul.Tartareosity.SubsystemTartareosity>();\nif (tart == null) return \"no-tartareosity-subsystem\";\ntart.CorpsificeSpawnData = new SAGhoul.Tartareosity.CorpsificeSpawnData(__GAME_TIME__, SAGhoul.Tartareosity.WorldType.Default, new Engine.Point3(10, 10, 10));\nreturn \"ok\";".Replace("__GAME_TIME__", gameTimeToSpawn.ToString("R", CultureInfo.InvariantCulture));
		return (string)(await context.PushCommandAsync(script)) ?? string.Empty;
	}
}