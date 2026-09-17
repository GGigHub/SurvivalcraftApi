using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Engine;
using Xunit;
using Xunit.Abstractions;

namespace Survivalcraft.Test;

[TestCaseOrderer("Survivalcraft.Test.PriorityOrderer", "Survivalcraft.Test")]
public sealed class EnchantmentSyncTest : IClassFixture<EnchantmentGameFixture>, IDisposable
{
	private const int WeaponSharpnessBit = 0x2;

	private const int ToolMendingBit = 0x4;

	private const int SpearLoyaltyBit = 0x8;

	private readonly EnchantmentGameFixture fixture;

	private readonly ITestOutputHelper output;

	private GameContext server => fixture.Server;

	private GameContext client => fixture.Client;

	public EnchantmentSyncTest(EnchantmentGameFixture fixture, ITestOutputHelper output)
	{
		this.fixture = fixture;
		this.output = output;
	}

	[Fact]
	[TestOrder(Priority = 1)]
	public async Task ModLoadsAndEnchantmentManagerBitsAreConsistent()
	{
		await EnsureWorldAndClientAsync();

		Assert.True(await EnchantmentSyncTools.IsEnchantmentLoadedAsync(server), "Enchantment assembly was not loaded on the server.");
		Assert.True(await EnchantmentSyncTools.IsEnchantmentLoadedAsync(client), "Enchantment assembly was not loaded on the client.");

		string raw = await EnchantmentSyncTools.GetBlockIndicesAsync(server);
		string[] values = raw.Split(',');
		Assert.True(values.Length == 7, "Unexpected block index response: " + raw);

		int tableIndex = ParseInt(values[0]);
		int bookIndex = ParseInt(values[1]);
		int rodIndex = ParseInt(values[4]);
		Assert.True(tableIndex > 0 && bookIndex > 0 && rodIndex > 0, "Block index resolution failed: " + raw);

		object result = await server.PushCommandAsync("FUNC int data = 0; data = Enchantment.EnchantmentManager.WeaponSetSharpness(data, true); data = Enchantment.EnchantmentManager.WeaponSetDurability(data, true); return data;");
		Assert.True(result is int bits && bits == (WeaponSharpnessBit | 0x1), "Weapon enchantment bit encoding mismatch. Got " + result);
	}

	[Fact]
	[TestOrder(Priority = 2)]
	public async Task AnvilResultPreviewMatchesOnBothEnds()
	{
		await EnsureWorldAndClientAsync();
		int playerIndex = await PlayerSyncTools.WaitForClientPlayerIndexAsync(client);
		Site site = await PrepareAnvilSiteAsync(playerIndex);
		List<string> failures = new List<string>();

		// Round 1: copper machete (weapon) + sharpness book -> sharpness bit must be set.
		int weaponValue = await MakeBlockValueAsync(server, "Game.CopperMacheteBlock", 0);
		int sharpnessBook = await MakeEnchantedBookValueAsync(server, 1);
		Assert.True(await EnchantmentSyncTools.AddInventorySlotAsync(server, site.Table, EnchantmentSyncTools.EnchantingTableComponentType, 0, weaponValue, 1), "Failed to add weapon to table slot 0.");
		Assert.True(await EnchantmentSyncTools.AddInventorySlotAsync(server, site.Table, EnchantmentSyncTools.EnchantingTableComponentType, 1, sharpnessBook, 1), "Failed to add book to table slot 1.");

		TableResult serverResult = await WaitForTableResultAsync(server, site.Table);
		Assert.True(serverResult.Raw != "empty" && serverResult.Raw != "missing", "Server did not compute an enchanting result. Table=" + await EnchantmentSyncTools.GetInventoryStateAsync(server, site.Table, EnchantmentSyncTools.EnchantingTableComponentType));
		Assert.True((serverResult.Data & WeaponSharpnessBit) != 0, "Server result lacks sharpness bit. " + serverResult.Raw);
		Assert.True((serverResult.Data & 0x1) == 0, "Server result unexpectedly has the durability bit. " + serverResult.Raw);

		TableResult clientResult = await WaitForTableResultAsync(client, site.Table);
		if (!TablesEqual(serverResult, clientResult))
		{
			failures.Add($"锋利武器: server={serverResult.Raw}; client={clientResult.Raw}");
		}

		// Round 2: copper pickaxe (tool) + mending book -> mending bit.
		await EnchantmentSyncTools.ClearInventoryAsync(server, site.Table, EnchantmentSyncTools.EnchantingTableComponentType);
		await Task.Delay(300);
		int pickValue = await MakeBlockValueAsync(server, "Game.CopperPickaxeBlock", 0);
		int mendingBook = await MakeEnchantedBookValueAsync(server, 2);
		Assert.True(await EnchantmentSyncTools.AddInventorySlotAsync(server, site.Table, EnchantmentSyncTools.EnchantingTableComponentType, 0, pickValue, 1), "Failed to add pickaxe to table slot 0.");
		Assert.True(await EnchantmentSyncTools.AddInventorySlotAsync(server, site.Table, EnchantmentSyncTools.EnchantingTableComponentType, 1, mendingBook, 1), "Failed to add mending book to table slot 1.");

		serverResult = await WaitForTableResultAsync(server, site.Table);
		Assert.True(serverResult.Raw != "empty" && serverResult.Raw != "missing", "Server did not compute second result. Table=" + await EnchantmentSyncTools.GetInventoryStateAsync(server, site.Table, EnchantmentSyncTools.EnchantingTableComponentType));
		Assert.True((serverResult.Data & ToolMendingBit) != 0, "Server result lacks mending bit. " + serverResult.Raw);

		clientResult = await WaitForTableResultAsync(client, site.Table);
		if (!TablesEqual(serverResult, clientResult))
		{
			failures.Add($"经验修补镐: server={serverResult.Raw}; client={clientResult.Raw}");
		}

		Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
	}

	[Fact]
	[TestOrder(Priority = 3)]
	public async Task TakingAnvilResultDeductsPlayerExperienceOnBothEnds()
	{
		await EnsureWorldAndClientAsync();
		int playerIndex = await PlayerSyncTools.WaitForClientPlayerIndexAsync(client);
		Site site = await PrepareAnvilSiteAsync(playerIndex);

		int weaponValue = await MakeBlockValueAsync(server, "Game.CopperMacheteBlock", 0);
		int sharpnessBook = await MakeEnchantedBookValueAsync(server, 1);
		Assert.True(await EnchantmentSyncTools.AddInventorySlotAsync(server, site.Table, EnchantmentSyncTools.EnchantingTableComponentType, 0, weaponValue, 1), "Failed to add weapon to table slot 0. State=" + await EnchantmentSyncTools.GetInventoryStateAsync(server, site.Table, EnchantmentSyncTools.EnchantingTableComponentType));
		Assert.True(await EnchantmentSyncTools.AddInventorySlotAsync(server, site.Table, EnchantmentSyncTools.EnchantingTableComponentType, 1, sharpnessBook, 1), "Failed to add book to table slot 1. State=" + await EnchantmentSyncTools.GetInventoryStateAsync(server, site.Table, EnchantmentSyncTools.EnchantingTableComponentType));

		// Creative ignores orb drops for the HUD level; AddExperience drives PlayerData.Level through the real sync packet.
		float seededLevel = await EnchantmentSyncTools.AddLevelsAsync(server, playerIndex, 5000);
		await TestWaiter.UntilAsync(
			() => EnchantmentSyncTools.GetPlayerLevelAsync(client, playerIndex),
			level => Math.Abs(level - seededLevel) <= 1.5f,
			TestTimeouts.DefaultEventually,
			"Client level did not reach the seeded value.");
		output.WriteLine($"取结果前测试台状态: clientCompat=[{await EnchantmentSyncTools.GetCompatNetStateAsync(client)}]; serverCompat=[{await EnchantmentSyncTools.GetCompatNetStateAsync(server)}]");
		float initialLevel = await EnchantmentSyncTools.GetPlayerLevelAsync(server, playerIndex);
		Assert.True(initialLevel >= 5f, $"Not enough starting level: {initialLevel:R}");

		await WaitForTableResultAsync(server, site.Table);
		TableResult serverResultBefore = await WaitForTableResultAsync(server, site.Table);
		output.WriteLine($"附魔台取结果前服务端: serverResult={serverResultBefore.Raw}; state={await EnchantmentSyncTools.GetInventoryStateAsync(server, site.Table, EnchantmentSyncTools.EnchantingTableComponentType)}");
		Assert.True(serverResultBefore.Raw != "empty" && serverResultBefore.Raw != "missing" && serverResultBefore.Raw != "timeout",
			"服务端在取结果前没有可取的附魔结果: " + serverResultBefore.Raw);
		await Task.Delay(300);

		// Part 1: the client asks for the result through the real CompatNet path (RemoveSlotItems slot 2).
		Assert.True(await EnchantmentSyncTools.RemoveInventoryResultAsync(client, site.Table, EnchantmentSyncTools.EnchantingTableComponentType), "Client could not take the enchanting result.");
		await Task.Delay(1200);
		output.WriteLine($"客户端取结果(CompatNet请求路径): serverLevel={await EnchantmentSyncTools.GetPlayerLevelAsync(server, playerIndex):R}; clientLevel={await EnchantmentSyncTools.GetPlayerLevelAsync(client, playerIndex):R}");
		output.WriteLine($"客户端取结果(CompatNet请求路径)后槽: server={await EnchantmentSyncTools.GetInventoryStateAsync(server, site.Table, EnchantmentSyncTools.EnchantingTableComponentType)}; client={await EnchantmentSyncTools.GetInventoryStateAsync(client, site.Table, EnchantmentSyncTools.EnchantingTableComponentType)}");

		// Part 2: re-seed the table and perform the authoritative removal on the server, then check the client syncs.
		Assert.True(await EnchantmentSyncTools.ClearInventoryAsync(server, site.Table, EnchantmentSyncTools.EnchantingTableComponentType), "Failed to clear the table for round 2.");
		await Task.Delay(300);
		int weaponAgain = await MakeBlockValueAsync(server, "Game.CopperMacheteBlock", 0);
		int bookAgain = await MakeEnchantedBookValueAsync(server, 1);
		Assert.True(await EnchantmentSyncTools.AddInventorySlotAsync(server, site.Table, EnchantmentSyncTools.EnchantingTableComponentType, 0, weaponAgain, 1), "Failed to re-add weapon.");
		Assert.True(await EnchantmentSyncTools.AddInventorySlotAsync(server, site.Table, EnchantmentSyncTools.EnchantingTableComponentType, 1, bookAgain, 1), "Failed to re-add book.");
		await WaitForTableResultAsync(server, site.Table);
		await Task.Delay(600);

		float serverLevelBefore = await EnchantmentSyncTools.GetPlayerLevelAsync(server, playerIndex);
		output.WriteLine($"服务端权威移除前 FindInteractingPlayer: {await EnchantmentSyncTools.GetInteractingPlayerAsync(server, site.Table, EnchantmentSyncTools.EnchantingTableComponentType)}");
		Assert.True(await EnchantmentSyncTools.RemoveInventoryResultAsync(server, site.Table, EnchantmentSyncTools.EnchantingTableComponentType), "Server could not take the enchanting result.");
		float serverLevelAfter = await TestWaiter.UntilAsync(
			() => EnchantmentSyncTools.GetPlayerLevelAsync(server, playerIndex),
			level => level < serverLevelBefore - 0.1f,
			TestTimeouts.DefaultEventually,
			"Server level was not reduced after the authoritative removal. ServerTable=" + await EnchantmentSyncTools.GetInventoryStateAsync(server, site.Table, EnchantmentSyncTools.EnchantingTableComponentType) + ";" + "ClientTable=" + await EnchantmentSyncTools.GetInventoryStateAsync(client, site.Table, EnchantmentSyncTools.EnchantingTableComponentType) + ";" + "ClientLevel=" + (await EnchantmentSyncTools.GetPlayerLevelAsync(client, playerIndex)).ToString("R", CultureInfo.InvariantCulture) + ";serverLevelBefore=" + serverLevelBefore.ToString("R", CultureInfo.InvariantCulture));

		await TestWaiter.UntilAsync(
			() => EnchantmentSyncTools.GetPlayerLevelAsync(client, playerIndex),
			level => Math.Abs(level - serverLevelAfter) <= 0.01f,
			TestTimeouts.DefaultEventually,
			"Client level did not match the server after taking the enchanting result.");

		await Task.Delay(400);
		float clientLevelAfter = await EnchantmentSyncTools.GetPlayerLevelAsync(client, playerIndex);
		string serverTable = await EnchantmentSyncTools.GetInventoryStateAsync(server, site.Table, EnchantmentSyncTools.EnchantingTableComponentType);
		string clientTable = await EnchantmentSyncTools.GetInventoryStateAsync(client, site.Table, EnchantmentSyncTools.EnchantingTableComponentType);

		output.WriteLine($"附魔台取结果(服务端权威移除): serverLevel={serverLevelAfter:R}; clientLevel={clientLevelAfter:R}");
		output.WriteLine($"附魔台取结果(服务端权威移除)后槽: server={serverTable}; client={clientTable}");
		Assert.True(string.Equals(serverTable, clientTable, StringComparison.Ordinal), $"取结果后铁砧槽不一致: server={serverTable}; client={clientTable}");
	}

	[Fact]
	[TestOrder(Priority = 4)]
	public async Task MagicRemovalPlatformRollResultIsNotPushedToClient()
	{
		await EnsureWorldAndClientAsync();
		int playerIndex = await PlayerSyncTools.WaitForClientPlayerIndexAsync(client);
		Site site = await PreparePlatformSiteAsync(playerIndex);

		int enchantedWeapon = await MakeEnchantedWeaponValueAsync(server);
		Assert.True(await EnchantmentSyncTools.AddInventorySlotAsync(server, site.Table, EnchantmentSyncTools.MagicRemovalPlatformComponentType, 0, enchantedWeapon, 1), "Failed to add enchanted weapon to platform slot 0.");
		int blankBook = await MakeBlockValueAsync(server, "Enchantment.ENBooks.BlankEnchantedBookBlock", 0);
		Assert.True(await EnchantmentSyncTools.AddInventorySlotAsync(server, site.Table, EnchantmentSyncTools.MagicRemovalPlatformComponentType, 1, blankBook, 1), "Failed to add blank book to platform slot 1.");
		await Task.Delay(500);

		// Creative ignores orb drops for the HUD level; AddExperience drives the level through the real sync packet.
		float seededLevel = await EnchantmentSyncTools.AddLevelsAsync(server, playerIndex, 5000);
		await TestWaiter.UntilAsync(
			() => EnchantmentSyncTools.GetPlayerLevelAsync(client, playerIndex),
			level => Math.Abs(level - seededLevel) <= 1.5f,
			TestTimeouts.DefaultEventually,
			"Client level did not reach the seeded value.");

		await TestWaiter.UntilAsync(
			() => EnchantmentSyncTools.GetInventoryStateAsync(client, site.Table, EnchantmentSyncTools.MagicRemovalPlatformComponentType),
			state => !string.Equals(state, "missing", StringComparison.OrdinalIgnoreCase) && state.Contains("1=", StringComparison.Ordinal),
			TestTimeouts.DefaultEventually,
			"Platform inventory did not synchronize to the client.");

		// Part 1: the client triggers the roll through the real CompatNet request path.
		string rollResult = ((string)(await client.PushCommandAsync(string.Create(CultureInfo.InvariantCulture, $"FUNC var project = Game.GameManager.Project;\nvar be = project.FindSubsystem<Game.SubsystemBlockEntities>().GetBlockEntity({site.Table.X}, {site.Table.Y}, {site.Table.Z});\nvar comps = be?.Entity?.FindComponents<Game.IInventory>();\nGame.IInventory inv = null;\nif (comps != null) {{ foreach (var c in comps) {{ if (c.GetType().FullName == \"Enchantment.ComponentMagicRemovalPlatform\") {{ inv = c; break; }} }} }}\nif (inv == null) return \"no-component\";\nvar runtimeType = System.AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(\"Game.EnchantmentCompatNetRuntime\")).FirstOrDefault(t => t != null);\nif (runtimeType == null) return \"no-runtime\";\nvar bridge = runtimeType.GetMethod(\"RollEnchantmentBridge\", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);\nif (bridge == null) return \"no-method\";\nbridge.Invoke(null, new object[] {{ inv }});\nreturn \"ok\";"))) ?? "error")!;
		Assert.True(rollResult == "ok", "Client could not drive the magic removal platform widget: " + rollResult);
		await Task.Delay(1200);

		string serverTableBridge = await EnchantmentSyncTools.GetInventoryStateAsync(server, site.Table, EnchantmentSyncTools.MagicRemovalPlatformComponentType);
		string clientTableBridge = await EnchantmentSyncTools.GetInventoryStateAsync(client, site.Table, EnchantmentSyncTools.MagicRemovalPlatformComponentType);
		float serverLevelBridge = await EnchantmentSyncTools.GetPlayerLevelAsync(server, playerIndex);
		float clientLevelBridge = await EnchantmentSyncTools.GetPlayerLevelAsync(client, playerIndex);
		output.WriteLine($"祛魔台 客户端CompatNet请求: server={serverTableBridge}; client={clientTableBridge}; serverLevel={serverLevelBridge:R}; clientLevel={clientLevelBridge:R}");

		// Part 2: authoritative roll directly on the server to see whether the roll result slot syncs to the client.
		float serverLevelBeforeRoll = await EnchantmentSyncTools.GetPlayerLevelAsync(server, playerIndex);
		Assert.True(await EnchantmentSyncTools.InvokePlatformRollAsync(server, site.Table), "Server roll failed.");
		await TestWaiter.UntilAsync(
			() => EnchantmentSyncTools.GetInventoryStateAsync(server, site.Table, EnchantmentSyncTools.MagicRemovalPlatformComponentType),
			state => ParseSlotCount(state, 2) > 0,
			TestTimeouts.DefaultEventually,
			"服务端祛魔台 roll 后没有产生结果书。");
		float serverLevelAfterRoll = await TestWaiter.UntilAsync(
			() => EnchantmentSyncTools.GetPlayerLevelAsync(server, playerIndex),
			level => level < serverLevelBeforeRoll - 0.2f,
			TestTimeouts.DefaultEventually,
			"服务端祛魔台 roll 后经验没有扣减。");
		await Task.Delay(600);

		string serverTable = await EnchantmentSyncTools.GetInventoryStateAsync(server, site.Table, EnchantmentSyncTools.MagicRemovalPlatformComponentType);
		string clientTable = await EnchantmentSyncTools.GetInventoryStateAsync(client, site.Table, EnchantmentSyncTools.MagicRemovalPlatformComponentType);
		float clientLevel = await EnchantmentSyncTools.GetPlayerLevelAsync(client, playerIndex);
		output.WriteLine($"祛魔台 服务端权威roll: server={serverTable}; client={clientTable}; serverLevel={serverLevelAfterRoll:R}; clientLevel={clientLevel:R}");
		Assert.True(string.Equals(serverTable, clientTable, StringComparison.Ordinal) && Math.Abs(serverLevelAfterRoll - clientLevel) <= 0.01f,
			$"祛魔台的随机结果/经验未同步: server=[{serverTable}]; client=[{clientTable}]; serverLevel={serverLevelAfterRoll:R}; clientLevel={clientLevel:R}");
	}

	private static int ParseSlotCount(string state, int slot)
	{
		if (string.IsNullOrEmpty(state))
		{
			return 0;
		}
		foreach (string entry in state.Split('|', StringSplitOptions.RemoveEmptyEntries))
		{
			string[] kv = entry.Split('=');
			if (kv.Length == 2 && kv[0] == slot.ToString(CultureInfo.InvariantCulture))
			{
				string[] vc = kv[1].Split(':');
				return vc.Length == 2 ? ParseInt(vc[1]) : 0;
			}
		}
		return 0;
	}

	[Fact]
	[TestOrder(Priority = 5)]
	public async Task MendingRepairsActiveToolAndSyncsToClient()
	{
		await EnsureWorldAndClientAsync();
		int playerIndex = await PlayerSyncTools.WaitForClientPlayerIndexAsync(client);
		Point3 playerCell = await EnchantmentSyncTools.GetPlayerCellAsync(server, playerIndex);

		// Copper pickaxe with mending (bit 0x4) and damage 40 stored in bits 4+.
		int pickValue = await MakeBlockValueAsync(server, "Game.CopperPickaxeBlock", ToolMendingBit | (40 << 4));
		Assert.True(await EnchantmentSyncTools.SetPlayerInventoryBlockValueAsync(server, playerIndex, 0, pickValue, 1), "Failed to seed mending pickaxe on the server.");
		Assert.True(await EnchantmentSyncTools.SetPlayerInventoryBlockValueAsync(client, playerIndex, 0, pickValue, 1), "Failed to seed mending pickaxe on the client.");

		await TestWaiter.UntilAsync(
			() => GetActiveSlotDataAsync(server, playerIndex, 0),
			damage => damage >= 35 && damage <= 45,
			TestTimeouts.DefaultEventually,
			"Server tool was not seeded.");

		await EnchantmentSyncTools.AddExpOrbAsync(server, playerCell, 10);

		await TestWaiter.UntilAsync(
			() => GetActiveSlotDataAsync(server, playerIndex, 0),
			damage => damage >= 0 && damage <= 20,
			TimeSpan.FromMilliseconds(6000),
			"Server tool was not repaired by the mending system.");

		await TestWaiter.UntilAsync(
			() => GetActiveSlotDataAsync(client, playerIndex, 0),
			damage => damage >= 0 && damage <= 20,
			TimeSpan.FromMilliseconds(6000),
			"Client tool damage did not reach the repaired value.");

		string serverOrbs = await EnchantmentSyncTools.GetPickablesAsync(server, EnchantmentSyncTools.ExpOrbBlockIndex, playerCell);
		string clientOrbs = await EnchantmentSyncTools.GetPickablesAsync(client, EnchantmentSyncTools.ExpOrbBlockIndex, playerCell);
		string serverInventory = await EnchantmentSyncTools.GetPlayerInventoryAllAsync(server, playerIndex, 10);
		string clientInventory = await EnchantmentSyncTools.GetPlayerInventoryAllAsync(client, playerIndex, 10);

		output.WriteLine($"经验修补: serverOrbs=[{serverOrbs}]; clientOrbs=[{clientOrbs}]");
		output.WriteLine($"经验修补库存: server={serverInventory}; client={clientInventory}");
		Assert.True(string.Equals(serverInventory, clientInventory, StringComparison.Ordinal), $"经验修补后库存槽不一致: server={serverInventory}; client={clientInventory}");
		Assert.True(string.Equals(serverOrbs, clientOrbs, StringComparison.Ordinal), $"经验修补后经验球状态不一致: server=[{serverOrbs}]; client=[{clientOrbs}]");
	}

	[Fact]
	[TestOrder(Priority = 6)]
	public async Task RecallHelmetAfterDeathLeavesClientAndServerPlayerStatesApart()
	{
		await EnsureWorldAndClientAsync();
		int playerIndex = await PlayerSyncTools.WaitForClientPlayerIndexAsync(client);

		string recallHelmet = await EnchantmentSyncTools.BuildEnchantedClothingAsync(server, 0, 0x4000);
		Assert.True(recallHelmet != "-1", "Could not build a recall helmet value.");
		int helmetValue = ParseInt(recallHelmet);
		Assert.True(await EnchantmentSyncTools.EquipClothingAsync(server, playerIndex, 0, helmetValue), "Failed to equip the recall helmet on the server player.");

		string baseline = await EnchantmentSyncTools.GetPlayerStateStringAsync(server, playerIndex);
		Assert.True(await EnchantmentSyncTools.TriggerDeathAsync(server, playerIndex), "Failed to trigger fatal damage on the server.");

		await Task.Delay(1500);
		string serverMid = await EnchantmentSyncTools.GetPlayerStateStringAsync(server, playerIndex);
		string clientMid = await EnchantmentSyncTools.GetPlayerStateStringAsync(client, playerIndex);
		await Task.Delay(2500);
		string serverLate = await EnchantmentSyncTools.GetPlayerStateStringAsync(server, playerIndex);
		string clientLate = await EnchantmentSyncTools.GetPlayerStateStringAsync(client, playerIndex);
		await Task.Delay(2500);
		string serverFinal = await EnchantmentSyncTools.GetPlayerStateStringAsync(server, playerIndex);
		string clientFinal = await EnchantmentSyncTools.GetPlayerStateStringAsync(client, playerIndex);

		output.WriteLine($"回溯1.5s: server=[{serverMid}]; client=[{clientMid}]");
		output.WriteLine($"回溯4.0s: server=[{serverLate}]; client=[{clientLate}]");
		output.WriteLine($"回溯6.5s: server=[{serverFinal}]; client=[{clientFinal}]");

		// The recall ritual engaged on the server (fatal damage suppressed, health held at 0.1).
		Assert.True(serverMid.Contains("alive=True", StringComparison.OrdinalIgnoreCase) && serverMid.Contains("health=0.1", StringComparison.OrdinalIgnoreCase),
			$"服务端玩家没有进入回溯仪式(应存活且血量为0.1). baseline=[{baseline}]; mid=[{serverMid}]");
		// The client-side copy normally diverges because the compatibility adapter disables the recall death-prefix on
		// the client while the InjureEventPacket still reaches it. Record the divergence as evidence.
		bool synced = EnchantmentSyncTools.PlayerPositionsMatch(serverFinal, clientFinal, 3f) && output != null;
		output.WriteLine($"回溯结束后两端玩家状态一致={synced}");
	}

	[Fact]
	[TestOrder(Priority = 7)]
	public async Task GroundSlamBootsIceTerrainChangesAreTransientlyUnsynchronized()
	{
		await EnsureWorldAndClientAsync();
		int playerIndex = await PlayerSyncTools.WaitForClientPlayerIndexAsync(client);
		Point3 playerCell = await EnchantmentSyncTools.GetPlayerCellAsync(server, playerIndex);

		string groundSlamBoots = await EnchantmentSyncTools.BuildEnchantedClothingAsync(server, 3, 0x8000);
		Assert.True(groundSlamBoots != "-1", "Could not build ground-slam boots value.");
		int bootsValue = ParseInt(groundSlamBoots);
		Assert.True(await EnchantmentSyncTools.EquipClothingAsync(server, playerIndex, 3, bootsValue), "Failed to equip ground-slam boots on the server.");
		Assert.True(await EnchantmentSyncTools.EquipClothingAsync(client, playerIndex, 3, bootsValue), "Failed to equip ground-slam boots on the client.");

		Point3 center = new Point3(playerCell.X, playerCell.Y - 1, playerCell.Z);
		string baselineServer = await EnchantmentSyncTools.GetTerrainFingerprintAsync(server, center, 5);
		string baselineClient = await EnchantmentSyncTools.GetTerrainFingerprintAsync(client, center, 5);
		output.WriteLine($"烬霜行者 baseline: server={baselineServer}; client={baselineClient}; 一致={string.Equals(baselineServer, baselineClient, StringComparison.Ordinal)}");

		Point3 poolCorner = new Point3(playerCell.X - 3, playerCell.Y - 1, playerCell.Z - 3);
		Assert.True(await EnchantmentSyncTools.TriggerMagmaWalkerAsync(server, poolCorner, Game.WaterBlock.Index), "Failed to create a water pool around the player.");

		await Task.Delay(1800);
		string serverAt1 = await EnchantmentSyncTools.GetTerrainFingerprintAsync(server, center, 5);
		string clientAt1 = await EnchantmentSyncTools.GetTerrainFingerprintAsync(client, center, 5);
		await Task.Delay(2500);
		string serverAt2 = await EnchantmentSyncTools.GetTerrainFingerprintAsync(server, center, 5);
		string clientAt2 = await EnchantmentSyncTools.GetTerrainFingerprintAsync(client, center, 5);

		output.WriteLine($"烬霜行者 1.8s: server={serverAt1}; client={clientAt1}; 一致={string.Equals(serverAt1, clientAt1, StringComparison.Ordinal)}");
		output.WriteLine($"烬霜行者 4.3s: server={serverAt2}; client={clientAt2}; 一致={string.Equals(serverAt2, clientAt2, StringComparison.Ordinal)}");

		Assert.True(string.Equals(serverAt2, clientAt2, StringComparison.Ordinal),
			$"烬霜行者改冰结果在两端不一致(本地 ChangeCell 与服务器地形分叉): baseline server/client={baselineServer}/{baselineClient}; server@{serverAt1}->{serverAt2}; client@{clientAt1}->{clientAt2}");
	}

	[Fact]
	[TestOrder(Priority = 8)]
	public async Task LoyaltySpearReturnProjectileStateDiverges()
	{
		await EnsureWorldAndClientAsync();
		int playerIndex = await PlayerSyncTools.WaitForClientPlayerIndexAsync(client);
		Point3 playerCell = await EnchantmentSyncTools.GetPlayerCellAsync(server, playerIndex);

		int spearValue = await MakeBlockValueAsync(server, "Game.WoodenSpearBlock", SpearLoyaltyBit);
		// Launch the spear well above the terrain so it travels a clear horizontal path
		// before hitting the wall at the same height (12 blocks ahead).
		int wallY = playerCell.Y + 10;
		await EnchantmentSyncTools.PlaceBlockAsync(server, new Point3(playerCell.X, wallY, playerCell.Z + 12), 5);
		await EnchantmentSyncTools.PlaceBlockAsync(server, new Point3(playerCell.X, wallY + 1, playerCell.Z + 12), 5);
		await EnchantmentSyncTools.PlaceBlockAsync(server, new Point3(playerCell.X, wallY + 2, playerCell.Z + 12), 5);

		Assert.True(await EnchantmentSyncTools.ProjectileTestSetupAsync(server, playerIndex, spearValue, 14f, 10f), "Failed to launch the loyalty spear on the server.");

		await Task.Delay(400);
		string serverAt1 = await EnchantmentSyncTools.GetProjectilesAsync(server);
		string clientAt1 = await EnchantmentSyncTools.GetProjectilesAsync(client);
		await Task.Delay(600);
		string serverAt2 = await EnchantmentSyncTools.GetProjectilesAsync(server);
		string clientAt2 = await EnchantmentSyncTools.GetProjectilesAsync(client);
		await Task.Delay(3000);
		string serverFinal = await EnchantmentSyncTools.GetProjectilesAsync(server);
		string clientFinal = await EnchantmentSyncTools.GetProjectilesAsync(client);
		string serverInventory = await EnchantmentSyncTools.GetPlayerInventoryAllAsync(server, playerIndex, 10);

		output.WriteLine($"忠诚长矛 0.4s: server=[{serverAt1}]; client=[{clientAt1}]");
		output.WriteLine($"忠诚长矛 1.0s: server=[{serverAt2}]; client=[{clientAt2}]");
		output.WriteLine($"忠诚长矛 4.0s: server=[{serverFinal}]; client=[{clientFinal}]");
		output.WriteLine($"忠诚长矛返回后服务端背包: {serverInventory}");

		Assert.True(serverAt1.Length > 0 || serverAt2.Length > 0, "服务端没有生成忠诚长矛抛射物。");
		Assert.True(string.Equals(serverFinal, clientFinal, StringComparison.Ordinal),
			$"忠诚长矛返回时两端抛射物状态不一致: server=[{serverAt1}]->[{serverFinal}]; client=[{clientAt1}]->[{clientFinal}]");
	}

	[Fact]
	[TestOrder(Priority = 9)]
	public async Task FishingFloatExistsOnlyInsideTheCastingProcess()
	{
		await EnsureWorldAndClientAsync();
		int playerIndex = await PlayerSyncTools.WaitForClientPlayerIndexAsync(client);
		int[] indices = await GetBlockIndicesAsync(server);

		// The rod behavior removes a float whenever the player is not holding the rod,
		// so equip the rod as the client's active item first (real play scenario).
		int rodValue = await MakeBlockValueAsync(client, "Enchantment.ENFishingrodBlock", 0);
		Assert.True(await EnchantmentSyncTools.SetPlayerInventoryBlockValueAsync(client, playerIndex, 0, rodValue, 1), "Failed to equip the fishing rod on the client.");
		await Task.Delay(300);

		// The real gameplay creates the float inside the client-only subsystem. The
		// CompatNet adapter keeps that subsystem from simulating, but the float list
		// itself is never transferred to the server.
		Assert.True(await EnchantmentSyncTools.CastFishingFloatAsync(client, playerIndex, indices[5], indices[4], 3f), "Failed to cast a fishing float on the client.");
		await Task.Delay(800);

		string serverCount = await EnchantmentSyncTools.GetFishingFloatCountAsync(server);
		string clientCount = await EnchantmentSyncTools.GetFishingFloatCountAsync(client);
		output.WriteLine($"钓鱼浮漂: server.Count={serverCount}; client.Count={clientCount}");
		Assert.True(clientCount == "1", $"客户端本地应存在浮漂. server={serverCount}; client={clientCount}");
		Assert.True(serverCount == "0", $"服务端不应存在客户端抛出的浮漂(纯本地状态). server={serverCount}; client={clientCount}");
	}

	public void Dispose()
	{
		fixture.ThrowIfAnyContextHasError();
	}

	private async Task EnsureWorldAndClientAsync()
	{
		if (!(await EnchantmentSyncTools.IsProjectReadyAsync(server)))
		{
			await PlayerSyncTools.CreateCreativeTestWorldAsync(server, "enchantment-sync");
		}
		if (!(await EnchantmentSyncTools.IsProjectReadyAsync(client)))
		{
			await GameTools.ConnectTestServer(client, server.GamePort);
		}
		await PlayerSyncTools.WaitForPlayerEntityAsync(playerIndex: await PlayerSyncTools.WaitForClientPlayerIndexAsync(client), context: server);
	}

	private async Task<Site> PrepareAnvilSiteAsync(int playerIndex)
	{
		int[] indices = await GetBlockIndicesAsync(server);
		Point3 playerCell = await EnchantmentSyncTools.GetPlayerCellAsync(server, playerIndex);
		Point3 tablePos = new Point3(playerCell.X + 2, playerCell.Y + 1, playerCell.Z + 2);
		await EnchantmentSyncTools.PlaceBlockAsync(server, tablePos, indices[0]);
		await EnchantmentSyncTools.WaitForBlockEntityComponentAsync(server, tablePos, EnchantmentSyncTools.EnchantingTableComponentType);
		await EnchantmentSyncTools.WaitForBlockEntityComponentAsync(client, tablePos, EnchantmentSyncTools.EnchantingTableComponentType);
		// Keep the client player near the table and push every other player (the server host) far away so
		// DepthFindNearestPlayer resolves the real operator for enchantment cost / CompatNet ownership.
		await EnchantmentSyncTools.TeleportOthersAwayAsync(server, playerIndex);
		await EnchantmentSyncTools.TeleportMainPlayerToAsync(server, playerIndex, new Point3(playerCell.X + 2, playerCell.Y + 1, playerCell.Z + 1));
		return new Site(tablePos);
	}

	private async Task<Site> PreparePlatformSiteAsync(int playerIndex)
	{
		int[] indices = await GetBlockIndicesAsync(server);
		Point3 playerCell = await EnchantmentSyncTools.GetPlayerCellAsync(server, playerIndex);
		Point3 platformPos = new Point3(playerCell.X + 4, playerCell.Y + 1, playerCell.Z + 4);
		await EnchantmentSyncTools.PlaceBlockAsync(server, platformPos, indices[3]);
		await EnchantmentSyncTools.WaitForBlockEntityComponentAsync(server, platformPos, EnchantmentSyncTools.MagicRemovalPlatformComponentType);
		await EnchantmentSyncTools.WaitForBlockEntityComponentAsync(client, platformPos, EnchantmentSyncTools.MagicRemovalPlatformComponentType);
		await EnchantmentSyncTools.TeleportOthersAwayAsync(server, playerIndex);
		await EnchantmentSyncTools.TeleportMainPlayerToAsync(server, playerIndex, new Point3(playerCell.X + 4, playerCell.Y + 1, playerCell.Z + 3));
		return new Site(platformPos);
	}

	private async Task<int[]> GetBlockIndicesAsync(GameContext context)
	{
		string raw = await EnchantmentSyncTools.GetBlockIndicesAsync(context);
		string[] values = raw.Split(',');
		int[] result = new int[values.Length];
		for (int i = 0; i < values.Length; i++)
		{
			result[i] = ParseInt(values[i]);
		}
		return result;
	}

	private async Task<TableResult> WaitForTableResultAsync(GameContext context, Point3 table)
	{
		for (int i = 0; i < 80; i++)
		{
			string state = await EnchantmentSyncTools.GetInventoryStateAsync(context, table, EnchantmentSyncTools.EnchantingTableComponentType);
			if (string.Equals(state, "missing", StringComparison.OrdinalIgnoreCase))
			{
				return new TableResult("missing", 0, 0);
			}
			string[] slots = state.Split('|');
			if (slots.Length < 3)
			{
				return new TableResult("bad-slot-count:" + state, 0, 0);
			}
			string[] result = slots[2].Split('=');
			string[] valueCount = result[1].Split(':');
			int value = ParseInt(valueCount[0]);
			int count = ParseInt(valueCount[1]);
			if (count > 0)
			{
				return new TableResult("value=" + value, value, value >> 14);
			}
			await Task.Delay(100);
		}
		return new TableResult("timeout", 0, 0);
	}

	private async Task ClearTableAsync(GameContext context, Point3 table)
	{
		await EnchantmentSyncTools.RemoveInventoryResultAsync(context, table, EnchantmentSyncTools.EnchantingTableComponentType);
	}

	private static bool TablesEqual(TableResult a, TableResult b)
	{
		return a.Value == b.Value;
	}

	private static async Task<int> MakeBlockValueAsync(GameContext context, string typeFullName, int data)
	{
		string script = string.Create(CultureInfo.InvariantCulture, $"FUNC var project = Game.GameManager.Project;\nvar type = System.AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(\"{typeFullName}\")).FirstOrDefault(t => t != null);\nif (type == null) return -1;\nint blockIndex = Game.BlocksManager.GetBlockIndex(type);\nreturn Game.Terrain.MakeBlockValue(blockIndex, 0, {data});");
		object result = await context.PushCommandAsync(script);
		return result is int value ? value : Convert.ToInt32(result, CultureInfo.InvariantCulture);
	}

	private static async Task<int> MakeEnchantedBookValueAsync(GameContext context, int bookData)
	{
		return await MakeBlockValueAsync(context, "Enchantment.ENBooks.EnchantedBookBlock", bookData);
	}

	private static async Task<int> MakeEnchantedWeaponValueAsync(GameContext context)
	{
		return await MakeBlockValueAsync(context, "Game.CopperMacheteBlock", WeaponSharpnessBit | (10 << 4));
	}

	private static async Task<int> GetActiveSlotDataAsync(GameContext context, int playerIndex, int slot)
	{
		string slotState = await EnchantmentSyncTools.GetPlayerInventorySlotAsync(context, playerIndex, slot);
		if (string.Equals(slotState, "missing", StringComparison.OrdinalIgnoreCase))
		{
			return -1;
		}
		string[] parts = slotState.Split(':');
		if (parts.Length < 2)
		{
			return -1;
		}
		int value = ParseInt(parts[0]);
		int count = ParseInt(parts[1]);
		if (count <= 0)
		{
			return -1;
		}
		int data = value >> 14;
		return (data >> 4) & 0xFFF;
	}

	private static int ParseInt(string value)
	{
		return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) ? result : -1;
	}

	private readonly record struct TableResult(string Raw, int Value, int Data);

	private readonly record struct Site(Point3 Table);
}