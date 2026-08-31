using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Engine;
using Xunit;

namespace Survivalcraft.Test;

[TestCaseOrderer("Survivalcraft.Test.PriorityOrderer", "Survivalcraft.Test")]
public sealed class CommandBlockSyncTest : IClassFixture<CommandBlockGameFixture>, IDisposable
{
	private readonly CommandBlockGameFixture fixture;

	private GameContext server => fixture.Server;

	private GameContext client => fixture.Client;

	public CommandBlockSyncTest(CommandBlockGameFixture fixture)
	{
		this.fixture = fixture;
	}

	[Fact]
	[TestOrder(Priority = 1)]
	public async Task CommandBlockModLoadsAndExecutesThroughItsRealSubmitPath()
	{
		await EnsureWorldAndClientAsync();
		GameContext context = client;
		CommandResult result = await CommandBlockSyncTools.ExecuteCommandAsync(context, await CommandBlockSyncTools.GetPlayerCellAsync(client), "gametime type:default");
		Assert.True(result.Succeeded, result.Raw);
	}

	[Fact]
	[TestOrder(Priority = 2)]
	public async Task BuildingAndCopyCommandsSynchronizeFromClientToServer()
	{
		await EnsureWorldAndClientAsync();
		Point3 origin = await CommandBlockSyncTools.GetPlayerCellAsync(client);
		(string, string)[] cases = new(string, string)[14]
		{
			("单方块", "place cd:@c type:default pos:0,0,0 id:46"),
			("连线", "place cd:@c type:line pos1:1,0,0 pos2:3,0,0 id:46"),
			("墙体", "place cd:@c type:frame pos1:5,0,0 pos2:8,3,0 id:46"),
			("球体", "place cd:@c type:sphere pos:12,2,0 id:46 r:2 con:false"),
			("圆柱", "place cd:@c type:column pos:18,0,0 id:46 r:2 h:3 opt:+y con:false"),
			("圆锥", "place cd:@c type:cone pos:24,0,0 id:46 r:2 h:3 opt:+y con:false"),
			("函数生成", "place cd:@c type:function pos:30,0,0 id:46 v:1 func1:x+y<z func2:null limx:0,1 limy:0,1 limz:0,1"),
			("区域替换", "replace cd:@c type:default pos1:0,0,0 pos2:3,0,0 id1:46 id2:5"),
			("复制", "copyblock cd:@c type:default pos1:0,0,0 pos2:1,0,0 pos3:38,0,0 pos4:39,0,0 con1:false con2:false"),
			("粘贴", "copyblock cd:@c type:copycache pos1:0,0,0 pos2:1,0,0 pos3:54,0,0"),
			("拼接", "copyblock cd:@c type:prolong pos1:0,0,0 pos2:1,0,0 con1:false con2:false"),
			("旋转", "copyblock cd:@c type:rotate pos1:0,0,0 pos2:1,0,0 pos3:42,0,0 opt1:+y opt2:+90 con:false"),
			("镜像", "copyblock cd:@c type:mirror pos1:0,0,0 pos2:1,0,0 pos3:46,0,0 opt:xoy con1:false con2:false"),
			("放大", "copyblock cd:@c type:enlarge pos1:0,0,0 pos2:1,0,0 pos3:50,0,0 con:false")
		};
		List<string> failures = new List<string>();
		(string, string)[] array = cases;
		for (int i = 0; i < array.Length; i++)
		{
			(string, string) tuple = array[i];
			string name = tuple.Item1;
			CommandResult result = await CommandBlockSyncTools.ExecuteCommandAsync(line: tuple.Item2, context: client, position: origin);
			await Task.Delay(100);
			string serverFingerprint = await CommandBlockSyncTools.GetTerrainFingerprintAsync(server, origin, 0, 0, 0, 56, 8, 8);
			string clientFingerprint = await CommandBlockSyncTools.GetTerrainFingerprintAsync(client, origin, 0, 0, 0, 56, 8, 8);
			if (!result.Succeeded || !string.Equals(serverFingerprint, clientFingerprint, StringComparison.Ordinal))
			{
				failures.Add($"{name}: result={result.Raw}; server={serverFingerprint}; client={clientFingerprint}");
			}
		}
		Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
	}

	[Fact]
	[TestOrder(Priority = 3)]
	public async Task DynamicBuildingCommandsSynchronizeMovingBlocks()
	{
		await EnsureWorldAndClientAsync();
		Point3 origin = await CommandBlockSyncTools.GetPlayerCellAsync(client);
		(string, string)[] cases = new(string, string)[3]
		{
			("moveblock", "moveblock cd:@c type:default pos1:0,0,0 pos2:1,0,0 vec3:6,0,0 v:50"),
			("moveset-create", "moveset cd:@c type:default pos1:0,0,0 pos2:1,0,0 pos3:8,0,0 opt:+x n:command-car"),
			("moveset-move", "moveset cd:@c type:move vec3:2,0,0 n:command-car v:50 con:false")
		};
		List<string> failures = new List<string>();
		(string, string)[] array = cases;
		for (int i = 0; i < array.Length; i++)
		{
			(string, string) tuple = array[i];
			string name = tuple.Item1;
			CommandResult result = await CommandBlockSyncTools.ExecuteCommandAsync(line: tuple.Item2, context: client, position: origin);
			string serverState = await CommandBlockSyncTools.GetMovingBlockStateAsync(server);
			string clientState = await CommandBlockSyncTools.GetMovingBlockStateAsync(client);
			if (!result.Succeeded || !string.Equals(serverState, clientState, StringComparison.Ordinal))
			{
				failures.Add($"{name}: result={result.Raw}; server={serverState}; client={clientState}");
			}
		}
		Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
	}

	[Fact]
	[TestOrder(Priority = 4)]
	public async Task CreatureCommandsSynchronizeSpawnRemovalDamageHealingAndFire()
	{
		await EnsureWorldAndClientAsync();
		Point3 origin = await CommandBlockSyncTools.GetPlayerCellAsync(client);
		CommandResult serverSpawn = await CommandBlockSyncTools.ExecuteCommandAsync(server, origin, "addnpc cd:@c type:default pos:0,0,0 obj:tiger");
		Assert.True(serverSpawn.Succeeded, serverSpawn.Raw);
		await CommandBlockSyncTools.WaitUntilContainsAsync(client, () => CommandBlockSyncTools.GetCreatureFingerprintAsync(client, origin), "Tiger");
		(string, string)[] cases = new(string, string)[5]
		{
			("刷怪", "addnpc cd:@c type:default pos:3,0,0 obj:tiger"),
			("伤害", "injure cd:@c type:default obj:tiger v:10"),
			("治疗", "heal cd:@c type:default obj:tiger v:10"),
			("着火", "catchfire cd:@c type:default obj:tiger v:10"),
			("移除", "removenpc cd:@c type:default obj:tiger")
		};
		List<string> failures = new List<string>();
		(string, string)[] array = cases;
		for (int num = 0; num < array.Length; num++)
		{
			(string, string) tuple = array[num];
			string name = tuple.Item1;
			CommandResult result = await CommandBlockSyncTools.ExecuteCommandAsync(line: tuple.Item2, context: client, position: origin);
			await Task.Delay(100);
			string serverState = await CommandBlockSyncTools.GetCreatureFingerprintAsync(server, origin);
			string clientState = await CommandBlockSyncTools.GetCreatureFingerprintAsync(client, origin);
			if (!result.Succeeded || !string.Equals(serverState, clientState, StringComparison.Ordinal))
			{
				failures.Add($"{name}: result={result.Raw}; server={serverState}; client={clientState}");
			}
		}
		Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
	}

	[Fact]
	[TestOrder(Priority = 5)]
	public async Task PlayerCommandsSynchronizeTeleportStatsActionsAndInvulnerability()
	{
		await EnsureWorldAndClientAsync();
		int playerIndex = await PlayerSyncTools.WaitForClientPlayerIndexAsync(client);
		Point3 origin = await CommandBlockSyncTools.GetPlayerCellAsync(client);
		(string, string)[] cases = new(string, string)[10]
		{
			("传送", "teleport cd:@c type:default pos:12,2,12"),
			("重生点", "spawn cd:@c type:default pos:14,2,14"),
			("等级", "level cd:@c type:default v:8"),
			("血量", "stats cd:@c type:health v:10"),
			("饥饿", "stats cd:@c type:food v:10"),
			("速度", "stats cd:@c type:speed v:10"),
			("强制跳跃", "action cd:@c type:jump v:1"),
			("移动", "action cd:@c type:moveup v:1"),
			("潜行机关", "action cd:@c type:sneak con:true"),
			("无敌", "action cd:@c type:invincible con:true")
		};
		List<string> failures = new List<string>();
		(string, string)[] array = cases;
		for (int i = 0; i < array.Length; i++)
		{
			(string, string) tuple = array[i];
			string name = tuple.Item1;
			CommandResult result = await CommandBlockSyncTools.ExecuteCommandAsync(line: tuple.Item2, context: client, position: origin);
			await Task.Delay(100);
			PlayerRuntimeState serverState = await PlayerSyncTools.GetPlayerStateAsync(server, playerIndex);
			PlayerRuntimeState clientState = await PlayerSyncTools.GetPlayerStateAsync(client, playerIndex);
			PlayerCommandState serverCommandState = await CommandBlockSyncTools.GetPlayerCommandStateAsync(server, playerIndex);
			PlayerCommandState clientCommandState = await CommandBlockSyncTools.GetPlayerCommandStateAsync(client, playerIndex);
			if (!result.Succeeded || !CommandBlockSyncTools.PlayerStatesMatch(serverState, clientState) || !serverCommandState.Matches(clientCommandState))
			{
				failures.Add($"{name}: result={result.Raw}; server={serverCommandState.Raw}; client={clientCommandState.Raw}");
			}
		}
		Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
	}

	[Fact]
	[TestOrder(Priority = 6)]
	public async Task MechanismConditionsAreEvaluatedByTheClientCommandBlock()
	{
		await EnsureWorldAndClientAsync();
		Point3 origin = await CommandBlockSyncTools.GetPlayerCellAsync(client);
		(string, string)[] cases = new(string, string)[16]
		{
			("检测方块", "if:blockexist type:default pos:0,0,0 id:0"),
			("检测生物", "if:entityexist type:default pos1:0,0,0 pos2:2,2,2 obj:player"),
			("检测掉落物", "if:dropexist pos1:0,0,0 pos2:2,2,2 id:0"),
			("检测物品", "if:itemexist type:default id:0"),
			("检测手持物", "if:handitem type:empty"),
			("检测等级", "if:levelrange vec2:0,100"),
			("检测高度", "if:heightrange vec2:-100,100"),
			("检测视角", "if:eyesangle eyes1:0,360 eyes2:-90,90"),
			("检测属性", "if:statsrange type:health vec2:0,100"),
			("检测动作", "if:actionmake type:default"),
			("检测界面", "if:openwidget type:default"),
			("检测模式", "if:gamemode type:creative"),
			("检测告示牌", "if:signtext type:default pos:0,0,0 text:test"),
			("检测点击", "if:clickinteract type:default pos:0,0,0 v:10"),
			("检测长按", "if:longpress v:1"),
			("检测时间", "if:timerange type:default vec2:0,4096")
		};
		List<string> failures = new List<string>();
		(string, string)[] array = cases;
		for (int i = 0; i < array.Length; i++)
		{
			(string, string) tuple = array[i];
			string name = tuple.Item1;
			CommandResult result = await CommandBlockSyncTools.ExecuteCommandAsync(line: tuple.Item2, context: client, position: origin, judge: true);
			if (!result.IsValid || result.Raw.Contains("result=Invalid", StringComparison.OrdinalIgnoreCase))
			{
				failures.Add(name + ": " + result.Raw);
			}
		}
		Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
	}

	[Fact]
	[TestOrder(Priority = 7)]
	public async Task InventoryCommandsSynchronizePlayerChestFurnaceAndDispenserChanges()
	{
		await EnsureWorldAndClientAsync();
		int playerIndex = await PlayerSyncTools.WaitForClientPlayerIndexAsync(client);
		Point3 origin = await CommandBlockSyncTools.GetPlayerCellAsync(client);
		await InventoryTestTools.SeedInventoryAsync(server, playerIndex, "0:0:0");
		await InventoryTestTools.AssertInventoryEventuallyAsync(client, playerIndex, "0:0:0");
		Point3 chest = origin + new Point3(4, 0, 0);
		Point3 furnace = origin + new Point3(6, 0, 0);
		Point3 dispenser = origin + new Point3(8, 0, 0);
		await CommandBlockSyncTools.CreateContainerAsync(server, chest, 27);
		await CommandBlockSyncTools.CreateContainerAsync(server, furnace, 45);
		await CommandBlockSyncTools.CreateContainerAsync(server, dispenser, 216);
		await CommandBlockSyncTools.WaitForContainerAsync(client, chest);
		await CommandBlockSyncTools.WaitForContainerAsync(client, furnace);
		await CommandBlockSyncTools.WaitForContainerAsync(client, dispenser);
		(string, string, Point3, int)[] cases = new(string, string, Point3, int)[9]
		{
			("背包添加", "additem cd:@c type:inventory id:5 s:1 v:3", origin, 10),
			("背包移除", "removeitem cd:@c type:inventory id:5 s:1 v:1", origin, 10),
			("箱子添加", "additem cd:@c type:chest pos:" + Relative(chest, origin) + " id:5 s:1 v:3", chest, 0),
			("箱子移除", "removeitem cd:@c type:chest pos:" + Relative(chest, origin) + " id:5 s:1 v:1", chest, 0),
			("熔炉添加", "additem cd:@c type:furnace pos:" + Relative(furnace, origin) + " id:5 s:1 v:3", furnace, 0),
			("熔炉移除", "removeitem cd:@c type:furnace pos:" + Relative(furnace, origin) + " s:1 v:1", furnace, 0),
			("发射器添加", "additem cd:@c type:dispenser pos:" + Relative(dispenser, origin) + " id:5 s:1 v:3", dispenser, 0),
			("发射器移除", "removeitem cd:@c type:dispenser pos:" + Relative(dispenser, origin) + " s:1 v:1", dispenser, 0),
			("箱子清空", "clearitem cd:@c type:chest pos:" + Relative(chest, origin), chest, 0)
		};
		List<string> failures = new List<string>();
		(string, string, Point3, int)[] array = cases;
		for (int i = 0; i < array.Length; i++)
		{
			(string, string, Point3, int) tuple = array[i];
			string name = tuple.Item1;
			string line = tuple.Item2;
			Point3 position = tuple.Item3;
			int slotIndex = tuple.Item4;
			CommandResult result = await CommandBlockSyncTools.ExecuteCommandAsync(client, origin, line);
			await Task.Delay(100);
			string text = ((!(position == origin)) ? (await CommandBlockSyncTools.GetContainerSlotAsync(server, position)) : (await CommandBlockSyncTools.GetPlayerInventorySlotAsync(server, playerIndex, slotIndex)));
			string serverState = text;
			string text2 = ((!(position == origin)) ? (await CommandBlockSyncTools.GetContainerSlotAsync(client, position)) : (await CommandBlockSyncTools.GetPlayerInventorySlotAsync(client, playerIndex, slotIndex)));
			string clientState = text2;
			if (!result.Succeeded || !string.Equals(serverState, clientState, StringComparison.Ordinal))
			{
				failures.Add($"{name}: result={result.Raw}; server={serverState}; client={clientState}");
			}
		}
		Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
	}

	[Fact]
	[TestOrder(Priority = 8)]
	public async Task WorldCommandsSynchronizeWeatherSkyTemperatureTimeAndGameMode()
	{
		await EnsureWorldAndClientAsync();
		Point3 origin = await CommandBlockSyncTools.GetPlayerCellAsync(client);
		(string, string)[] cases = new(string, string)[9]
		{
			("天气", "weather cd:@c type:default con:true color:255,0,0"),
			("降水", "weather cd:@c type:precipitation con:true"),
			("雾", "weather cd:@c type:fog con:true"),
			("天空颜色", "skycolor cd:@c type:default color:255,0,0"),
			("温度", "temperature cd:@c type:default pos1:0,0,0 pos2:2,0,2 v:5"),
			("湿度", "humidity cd:@c type:default pos1:0,0,0 pos2:2,0,2 v:5"),
			("时间加速", "gametime cd:@c type:accelerate v:10"),
			("时间速度", "gametime cd:@c type:slow v:2"),
			("游戏模式", "gamemode cd:@c type:creative")
		};
		List<string> failures = new List<string>();
		(string, string)[] array = cases;
		for (int i = 0; i < array.Length; i++)
		{
			(string, string) tuple = array[i];
			string name = tuple.Item1;
			CommandResult result = await CommandBlockSyncTools.ExecuteCommandAsync(line: tuple.Item2, context: client, position: origin);
			await Task.Delay(100);
			string serverState = await CommandBlockSyncTools.GetWorldStateAsync(server, origin);
			string clientState = await CommandBlockSyncTools.GetWorldStateAsync(client, origin);
			if (!result.Succeeded || !string.Equals(serverState, clientState, StringComparison.Ordinal))
			{
				failures.Add($"{name}: result={result.Raw}; server={serverState}; client={clientState}");
			}
		}
		Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
	}

	[Fact]
	[TestOrder(Priority = 9)]
	public async Task PresentationAndScriptCommandsExecuteInTheClientPresentationLayer()
	{
		await EnsureWorldAndClientAsync();
		Point3 origin = await CommandBlockSyncTools.GetPlayerCellAsync(client);
		string commandPath = await CommandBlockSyncTools.GetCommandDirectoryAsync(client);
		Assert.True(Directory.Exists(commandPath), "Command resource directory was not created: " + commandPath);
		File.WriteAllText(Path.Combine(commandPath, "sync-test-commands.txt"), "gametime type:default\n");
		File.WriteAllText(Path.Combine(commandPath, "sync-test-script.js"), "var commandBlockSyncTest = 1;\n");
		(string, string)[] cases = new(string, string)[10]
		{
			("相机控制", "camera cd:@c type:lock"),
			("粒子", "particle cd:@c type:fire pos:0,0,0 size:10 count:3"),
			("音效", "audio cd:@c type:default f:duck.wav v:10 p:8"),
			("音乐", "music cd:@c type:default f:duck.wav"),
			("图片贴图", "texture cd:@c type:block f:standard.png"),
			("像素画", "image cd:@c type:default pos:0,0,0 con:false f:melon.webp"),
			("光点贴图", "pattern cd:@c type:default pos:0,0,0 color:255,255,255 vec3:0,0,0 v:10 f:melon.webp opt:default"),
			("建筑文件", "build cd:@c type:default pos:0,0,0 f:clownhouse.txt opt:x-y-z"),
			("命令文本", "cmdtext cd:@c type:execute f:sync-test-commands.txt"),
			("JavaScript", "cmdtext cd:@c type:javascript f:sync-test-script.js")
		};
		List<string> failures = new List<string>();
		(string, string)[] array = cases;
		for (int i = 0; i < array.Length; i++)
		{
			(string, string) tuple = array[i];
			string name = tuple.Item1;
			CommandResult result = await CommandBlockSyncTools.ExecuteCommandAsync(line: tuple.Item2, context: client, position: origin);
			await Task.Delay(100);
			if (!result.Succeeded)
			{
				failures.Add(name + ": " + result.Raw);
			}
		}
		Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
	}

	[Fact]
	[TestOrder(Priority = 10)]
	public async Task AdvancedPropertyCommandsExecuteThroughTheClientSubmitPath()
	{
		await EnsureWorldAndClientAsync();
		Point3 origin = await CommandBlockSyncTools.GetPlayerCellAsync(client);
		(string, string)[] cases = new(string, string)[3]
		{
			("方块属性", "blockdata cd:@c type:is-collidable id:1 con:false"),
			("生物属性", "creaturedata cd:@c type:walk-speed obj:player v:20"),
			("衣物属性", "clothesdata cd:@c type:armor-protection cid:0 v:20")
		};
		List<string> failures = new List<string>();
		(string, string)[] array = cases;
		for (int i = 0; i < array.Length; i++)
		{
			(string, string) tuple = array[i];
			string name = tuple.Item1;
			CommandResult result = await CommandBlockSyncTools.ExecuteCommandAsync(line: tuple.Item2, context: client, position: origin);
			if (!result.Succeeded)
			{
				failures.Add(name + ": " + result.Raw);
			}
		}
		Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
	}

	public void Dispose()
	{
		fixture.ThrowIfAnyContextHasError();
	}

	private async Task EnsureWorldAndClientAsync()
	{
		if (!(await CommandBlockSyncTools.IsProjectReadyAsync(server)))
		{
			await PlayerSyncTools.CreateCreativeTestWorldAsync(server, "command-block-sync");
		}
		if (!(await CommandBlockSyncTools.IsProjectReadyAsync(client)))
		{
			await GameTools.ConnectTestServer(client, server.GamePort);
		}
		await PlayerSyncTools.WaitForPlayerEntityAsync(playerIndex: await PlayerSyncTools.WaitForClientPlayerIndexAsync(client), context: server);
	}

	private static string Relative(Point3 point, Point3 origin)
	{
		IFormatProvider invariantCulture = CultureInfo.InvariantCulture;
		DefaultInterpolatedStringHandler handler = new DefaultInterpolatedStringHandler(2, 3, invariantCulture);
		handler.AppendFormatted(point.X - origin.X);
		handler.AppendLiteral(",");
		handler.AppendFormatted(point.Y - origin.Y);
		handler.AppendLiteral(",");
		handler.AppendFormatted(point.Z - origin.Z);
		return string.Create(invariantCulture, ref handler);
	}
}


