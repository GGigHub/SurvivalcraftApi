using System;
using System.Globalization;
using System.Threading.Tasks;
using Engine;
using Xunit;

namespace Survivalcraft.Test;

internal static class EnchantmentSyncTools
{
	public const string EnchantingTableComponentType = "Enchantment.ComponentEnchantingTable";

	public const string MagicRemovalPlatformComponentType = "Enchantment.ComponentMagicRemovalPlatform";

	public const int ExpOrbBlockIndex = 248;

	public static async Task<bool> IsEnchantmentLoadedAsync(GameContext context)
	{
		object? result = await context.PushCommandAsync("FUNC return System.AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == \"Enchantment\");");
		return result is bool b && b;
	}

	public static async Task<bool> IsProjectReadyAsync(GameContext context)
	{
		object? result = await context.PushCommandAsync("FUNC return Game.GameManager.Project != null;");
		return result is bool b && b;
	}

	public static async Task<string> GetBlockIndicesAsync(GameContext context)
	{
		object? result = await context.PushCommandAsync("FUNC return string.Join(\",\", Game.BlocksManager.GetBlockIndex<Enchantment.EnchantingTableBlock>(false, false), Game.BlocksManager.GetBlockIndex<Enchantment.ENBooks.EnchantedBookBlock>(false, false), Game.BlocksManager.GetBlockIndex<Enchantment.ENBooks.BlankEnchantedBookBlock>(false, false), Game.BlocksManager.GetBlockIndex<Enchantment.MagicRemovalPlatformBlock>(false, false), Game.BlocksManager.GetBlockIndex<Enchantment.ENFishingrodBlock>(false, false), Game.BlocksManager.GetBlockIndex<Enchantment.ENBobberBlock>(false, false), Game.BlocksManager.GetBlockIndex<Enchantment.ENClothing.ENClothingBlock>(false, false));");
		return result as string ?? string.Empty;
	}

	public static async Task<Point3> GetPlayerCellAsync(GameContext context, int playerIndex)
	{
		object? result = await context.PushCommandAsync($$"""
		FUNC var players = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>();
		var pd = players.PlayersData.FirstOrDefault(p => p.PlayerIndex == {{playerIndex}});
		if (pd?.ComponentPlayer == null) return "missing";
		var pos = pd.ComponentPlayer.ComponentBody.Position;
		var c = Game.Terrain.ToCell(pos);
		return string.Join(",", c.X, c.Y, c.Z);
		""");
		string raw = result as string ?? "missing";
		if (string.Equals(raw, "missing", StringComparison.OrdinalIgnoreCase))
		{
			return new Point3(0, 0, 0);
		}
		string[] values = raw.Split(',');
		return new Point3(int.Parse(values[0], CultureInfo.InvariantCulture), int.Parse(values[1], CultureInfo.InvariantCulture), int.Parse(values[2], CultureInfo.InvariantCulture));
	}

	public static async Task<bool> TeleportMainPlayerToAsync(GameContext context, int playerIndex, Point3 cell)
	{
		object? result = await context.PushCommandAsync($$"""
		FUNC var players = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>();
		var pd = players.PlayersData.FirstOrDefault(p => p.PlayerIndex == {{playerIndex}});
		if (pd?.ComponentPlayer == null) return "missing";
		var body = pd.ComponentPlayer.ComponentBody;
		body.Position = new Engine.Vector3((float){{cell.X}} + 0.5f, (float){{cell.Y}} + 1.6f, (float){{cell.Z}} + 0.5f);
		body.Velocity = Engine.Vector3.Zero;
		return "ok";
		""");
		return (result as string) == "ok";
	}

	public static async Task TeleportOthersAwayAsync(GameContext context, int keepPlayerIndex)
	{
		await context.PushCommandAsync($$"""
		FUNC var players = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>();
		var far = players.GlobalSpawnPosition + new Engine.Vector3(600f, 80f, 600f);
		foreach (var pd in players.PlayersData) {
			if (pd.PlayerIndex != {{keepPlayerIndex}} && pd.ComponentPlayer != null && pd.ComponentPlayer.ComponentBody != null) {
				pd.ComponentPlayer.ComponentBody.Position = far;
				pd.ComponentPlayer.ComponentBody.Velocity = Engine.Vector3.Zero;
			}
		}
		return true;
		""");
	}

	public static async Task PlaceBlockAsync(GameContext context, Point3 point, int blockIndex)
	{
		object? result = await context.PushCommandAsync($$"""
		FUNC var terrain = Game.GameManager.Project.FindSubsystem<Game.SubsystemTerrain>();
		terrain.ChangeCell({{point.X}}, {{point.Y}}, {{point.Z}}, Game.Terrain.MakeBlockValue({{blockIndex}}));
		return true;
		""");
		Assert.True(result is bool b && b, $"Failed to place block {blockIndex} at {point}.");
	}

	public static async Task WaitForBlockEntityComponentAsync(GameContext context, Point3 point, string componentFullTypeName)
	{
		string script = $$"""
		FUNC var project = Game.GameManager.Project;
		var be = project.FindSubsystem<Game.SubsystemBlockEntities>().GetBlockEntity({{point.X}}, {{point.Y}}, {{point.Z}});
		if (be == null) return false;
		var comps = be.Entity?.FindComponents<Game.IInventory>();
		if (comps == null) return false;
		foreach (var c in comps) {
			if (c.GetType().FullName == "{{componentFullTypeName}}") return true;
		}
		return false;
		""";
		for (int i = 0; i < 100; i++)
		{
			object? result = await context.PushCommandAsync(script);
			if (result is bool value && value)
			{
				return;
			}
			await Task.Delay(100);
		}
		Assert.Fail($"{componentFullTypeName} was not synchronized to {context.RoleName} at {point}.");
	}

	public static async Task<bool> AddInventorySlotAsync(GameContext context, Point3 point, string componentFullTypeName, int slot, int value, int count)
	{
		object? result = await context.PushCommandAsync($$"""
		FUNC var project = Game.GameManager.Project;
		var be = project.FindSubsystem<Game.SubsystemBlockEntities>().GetBlockEntity({{point.X}}, {{point.Y}}, {{point.Z}});
		var comps = be?.Entity?.FindComponents<Game.IInventory>();
		if (comps == null) return false;
		Game.IInventory inv = null;
		foreach (var c in comps) {
			if (c.GetType().FullName == "{{componentFullTypeName}}") { inv = c; break; }
		}
		if (inv == null) return false;
		inv.RemoveSlotItems({{slot}}, inv.GetSlotCount({{slot}}));
		inv.AddSlotItems({{slot}}, {{value}}, {{count}});
		return inv.GetSlotCount({{slot}}) > 0;
		""");
		return result is bool value2 && value2;
	}

	public static async Task<bool> ClearInventoryAsync(GameContext context, Point3 point, string componentFullTypeName)
	{
		object? result = await context.PushCommandAsync($$"""
		FUNC var project = Game.GameManager.Project;
		var be = project.FindSubsystem<Game.SubsystemBlockEntities>().GetBlockEntity({{point.X}}, {{point.Y}}, {{point.Z}});
		var comps = be?.Entity?.FindComponents<Game.IInventory>();
		if (comps == null) return false;
		Game.IInventory inv = null;
		foreach (var c in comps) {
			if (c.GetType().FullName == "{{componentFullTypeName}}") { inv = c; break; }
		}
		if (inv == null) return false;
		for (int i = 0; i < inv.SlotsCount; i++) {
			inv.RemoveSlotItems(i, inv.GetSlotCount(i));
		}
		return true;
		""");
		return result is bool value && value;
	}

	public static async Task<bool> RemoveInventoryResultAsync(GameContext context, Point3 point, string componentFullTypeName)
	{
		object? result = await context.PushCommandAsync($$"""
		FUNC var project = Game.GameManager.Project;
		var be = project.FindSubsystem<Game.SubsystemBlockEntities>().GetBlockEntity({{point.X}}, {{point.Y}}, {{point.Z}});
		var comps = be?.Entity?.FindComponents<Game.IInventory>();
		if (comps == null) return false;
		Game.IInventory inv = null;
		foreach (var c in comps) {
			if (c.GetType().FullName == "{{componentFullTypeName}}") { inv = c; break; }
		}
		if (inv == null) return false;
		inv.RemoveSlotItems(2, 1);
		return true;
		""");
		return result is bool value && value;
	}

	public static async Task<string> GetInventoryStateAsync(GameContext context, Point3 point, string componentFullTypeName)
	{
		object? result = await context.PushCommandAsync($$"""
		FUNC var project = Game.GameManager.Project;
		var be = project.FindSubsystem<Game.SubsystemBlockEntities>().GetBlockEntity({{point.X}}, {{point.Y}}, {{point.Z}});
		var comps = be?.Entity?.FindComponents<Game.IInventory>();
		Game.IInventory inv = null;
		if (comps != null) {
			foreach (var c in comps) {
				if (c.GetType().FullName == "{{componentFullTypeName}}") { inv = c; break; }
			}
		}
		if (inv == null) return "missing";
		var parts = new System.Collections.Generic.List<string>();
		for (int i = 0; i < inv.SlotsCount; i++) {
			parts.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}={1}:{2}", i, inv.GetSlotValue(i), inv.GetSlotCount(i)));
		}
		return string.Join("|", parts);
		""");
		return result as string ?? "missing";
	}

	public static async Task<float> GetPlayerLevelAsync(GameContext context, int playerIndex)
	{
		object? result = await context.PushCommandAsync($$"""
		FUNC var players = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>();
		return players.PlayersData.FirstOrDefault(p => p.PlayerIndex == {{playerIndex}})?.Level ?? -1f;
		""");
		if (result == null)
		{
			return -1f;
		}
		return ConvertToFloat(result);
	}

	public static async Task<string> GetCompatNetStateAsync(GameContext context)
	{
		object? result = await context.PushCommandAsync("FUNC var coreType = System.AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(\"Game.CompatNetCore\")).FirstOrDefault(t => t != null);\nif (coreType == null) return \"no-core\";\nvar ready = (bool)(coreType.GetProperty(\"NetworkBridgeReady\").GetValue(null) ?? false);\nreturn \"ready=\" + ready + \";client=\" + Game.NetworkManager.IsClientRunning + \";server=\" + Game.NetworkManager.IsServerRunning;");
		return result as string ?? "missing";
	}

	public static async Task<string> GetInteractingPlayerAsync(GameContext context, Point3 point, string componentFullTypeName)
	{
		object? result = await context.PushCommandAsync($$"""
		FUNC var project = Game.GameManager.Project;
		var be = project.FindSubsystem<Game.SubsystemBlockEntities>().GetBlockEntity({{point.X}}, {{point.Y}}, {{point.Z}});
		var comps = be?.Entity?.FindComponents<Game.IInventory>();
		Game.IInventory inv = null;
		if (comps != null) {
			foreach (var c in comps) {
				if (c.GetType().FullName == "{{componentFullTypeName}}") { inv = c; break; }
			}
		}
		if (inv == null) return "no-inventory";
		var method = inv.GetType().GetMethod("FindInteractingPlayer", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
		if (method == null) return "no-method";
		var player = method.Invoke(inv, null);
		string found = player == null ? "null" : "player:" + ((Game.ComponentPlayer)player).PlayerData.PlayerIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
		int playersCount = project.FindSubsystem<Game.SubsystemPlayers>().ComponentPlayers.Count;
		return "found=" + found + ";componentPlayers=" + playersCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
		""");
		return result as string ?? "missing";
	}

	public static async Task<bool> InvokePlatformRollAsync(GameContext context, Point3 point)
	{
		object? result = await context.PushCommandAsync($$"""
		FUNC var project = Game.GameManager.Project;
		var be = project.FindSubsystem<Game.SubsystemBlockEntities>().GetBlockEntity({{point.X}}, {{point.Y}}, {{point.Z}});
		var comps = be?.Entity?.FindComponents<Game.IInventory>();
		if (comps == null) return false;
		Game.IInventory inv = null;
		foreach (var c in comps) {
			if (c.GetType().FullName == "Enchantment.ComponentMagicRemovalPlatform") { inv = c; break; }
		}
		if (inv == null) return false;
		var method = inv.GetType().GetMethod("RollEnchantment", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
		if (method == null) return false;
		method.Invoke(inv, null);
		return true;
		""");
		return result is bool value && value;
	}

	public static async Task<float> AddLevelsAsync(GameContext context, int playerIndex, int count)
	{
		object? result = await context.PushCommandAsync($$"""
		FUNC var players = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>();
		var pd = players.PlayersData.FirstOrDefault(p => p.PlayerIndex == {{playerIndex}});
		if (pd?.ComponentPlayer == null) return -1f;
		var level = pd.ComponentPlayer.ComponentLevel;
		if (level == null) return -1f;
		level.AddExperience({{count}}, false);
		return pd.Level;
		""");
		if (result == null)
		{
			return -1f;
		}
		return ConvertToFloat(result);
	}

	public static async Task<string> GetPlayerStateStringAsync(GameContext context, int playerIndex)
	{
		object? result = await context.PushCommandAsync($$"""
		FUNC var players = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>();
		var pd = players.PlayersData.FirstOrDefault(p => p.PlayerIndex == {{playerIndex}});
		if (pd?.ComponentPlayer == null) return "missing";
		var body = pd.ComponentPlayer.ComponentBody;
		return string.Format(System.Globalization.CultureInfo.InvariantCulture, "x={0:R};y={1:R};z={2:R};health={3:R};alive={4};state={5}", body.Position.X, body.Position.Y, body.Position.Z, pd.ComponentPlayer.ComponentHealth.Health, pd.ComponentPlayer.ComponentHealth.Health > 0f, pd.m_stateMachine != null ? (pd.m_stateMachine.CurrentState ?? string.Empty) : string.Empty);
		""");
		return result as string ?? "missing";
	}

	public static async Task<string> GetPlayerInventorySlotAsync(GameContext context, int playerIndex, int slot)
	{
		object? result = await context.PushCommandAsync($$"""
		FUNC var players = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>();
		var pd = players.PlayersData.FirstOrDefault(p => p.PlayerIndex == {{playerIndex}});
		var inv = pd?.ComponentPlayer?.ComponentMiner?.Inventory;
		if (inv == null) return "missing";
		return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}:{1}:{2}", inv.GetSlotValue({{slot}}), inv.GetSlotCount({{slot}}), inv.ActiveSlotIndex);
		""");
		return result as string ?? "missing";
	}

	public static async Task<bool> SetPlayerInventoryBlockValueAsync(GameContext context, int playerIndex, int slot, int value, int count)
	{
		object? result = await context.PushCommandAsync($$"""
		FUNC var players = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>();
		var pd = players.PlayersData.FirstOrDefault(p => p.PlayerIndex == {{playerIndex}});
		var inv = pd?.ComponentPlayer?.ComponentMiner?.Inventory;
		if (inv == null) return "missing";
		inv.RemoveSlotItems({{slot}}, inv.GetSlotCount({{slot}}));
		if ({{count}} > 0) {
			inv.AddSlotItems({{slot}}, {{value}}, {{count}});
		}
		inv.ActiveSlotIndex = {{slot}};
		return "ok";
		""");
		return (result as string) == "ok";
	}

	public static async Task<string> GetPlayerInventoryAllAsync(GameContext context, int playerIndex, int maxSlots)
	{
		object? result = await context.PushCommandAsync($$"""
		FUNC var players = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>();
		var pd = players.PlayersData.FirstOrDefault(p => p.PlayerIndex == {{playerIndex}});
		var inv = pd?.ComponentPlayer?.ComponentMiner?.Inventory;
		if (inv == null) return "missing";
		var parts = new System.Collections.Generic.List<string>();
		for (int i = 0; i < System.Math.Min(inv.SlotsCount, {{maxSlots}}); i++) {
			parts.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}:{1}:{2}", i, inv.GetSlotValue(i), inv.GetSlotCount(i)));
		}
		return "active=" + inv.ActiveSlotIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) + ";" + string.Join(";", parts);
		""");
		return result as string ?? "missing";
	}

	public static async Task<string> GetPickablesAsync(GameContext context, int contents, Point3 near)
	{
		object? result = await context.PushCommandAsync($$"""
		FUNC var project = Game.GameManager.Project;
		var center = new Engine.Vector3((float){{near.X}} + 0.5f, (float){{near.Y}} + 0.5f, (float){{near.Z}} + 0.5f);
		var list = project.FindSubsystem<Game.SubsystemPickables>().Pickables
		    .Where(p => Game.Terrain.ExtractContents(p.Value) == {{contents}} && Engine.Vector3.DistanceSquared(p.Position, center) <= 256f)
		    .Select(p => string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}:{1}:{2:R}", p.Id, p.Count, p.ToRemove))
		    .OrderBy(s => s);
		return string.Join(";", list);
		""");
		return result as string ?? string.Empty;
	}

	public static async Task<int> AddExpOrbAsync(GameContext context, Point3 near, int count)
	{
		object? result = await context.PushCommandAsync($$"""
		FUNC var project = Game.GameManager.Project;
		var position = new Engine.Vector3((float){{near.X}} + 0.5f, (float){{near.Y}} + 1f, (float){{near.Z}} + 0.5f);
		var pickable = project.FindSubsystem<Game.SubsystemPickables>().AddPickable(Game.Terrain.MakeBlockValue(248, 0, 0), {{count}}, position, Engine.Vector3.Zero, (Engine.Matrix?)null);
		pickable.MaxTimeExist = 9999f;
		return pickable.Id;
		""");
		return result is int id ? id : Convert.ToInt32(result, CultureInfo.InvariantCulture);
	}

	public static async Task<string> GetProjectilesAsync(GameContext context)
	{
		object? result = await context.PushCommandAsync("FUNC var project = Game.GameManager.Project;\nvar list = project.FindSubsystem<Game.SubsystemProjectiles>().Projectiles;\nvar parts = new System.Collections.Generic.List<string>();\nint idx = 0;\nforeach (var p in list) {\n    parts.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, \"{0}:({1:R},{2:R},{3:R})/({4:R},{5:R},{6:R})/tc={7}/bc={8}/rem={9}\", idx, p.Position.X, p.Position.Y, p.Position.Z, p.Velocity.X, p.Velocity.Y, p.Velocity.Z, p.TerrainCollidable, p.BodyCollidable, p.ToRemove));\n    idx++;\n}\nreturn string.Join(\";\", parts.ToArray());");
		return result as string ?? string.Empty;
	}

	public static async Task<string> GetFishingFloatCountAsync(GameContext context)
	{
		object? result = await context.PushCommandAsync("FUNC var project = Game.GameManager.Project;\nvar subsystemType = System.AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(\"Enchantment.ENSubsystemFishingFloats\")).FirstOrDefault(t => t != null);\nif (subsystemType == null) return \"no-subsystem\";\nvar subsystem = project.FindSubsystem(subsystemType, null, false);\nif (subsystem == null) return \"missing\";\nvar property = subsystemType.GetProperty(\"Floats\");\nif (property == null) return \"no-property\";\nvar floats = property.GetValue(subsystem) as System.Collections.IEnumerable;\nif (floats == null) return \"0\";\nint count = 0;\nforeach (var f in floats) { count++; }\nreturn count.ToString(System.Globalization.CultureInfo.InvariantCulture);");
		return result as string ?? "missing";
	}

	public static async Task<bool> CastFishingFloatAsync(GameContext context, int playerIndex, int bobberIndex, int rodIndex, float distanceX)
	{
		string dx = distanceX.ToString("R", CultureInfo.InvariantCulture);
		object? result = await context.PushCommandAsync($$"""
		FUNC var project = Game.GameManager.Project;
		var pd = project.FindSubsystem<Game.SubsystemPlayers>().PlayersData.FirstOrDefault(p => p.PlayerIndex == {{playerIndex}});
		if (pd?.ComponentPlayer == null) return false;
		var owner = pd.ComponentPlayer.Entity.FindComponent<Game.ComponentCreature>();
		if (owner == null) return false;
		var subsystemType = System.AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Enchantment.ENSubsystemFishingFloats")).FirstOrDefault(t => t != null);
		if (subsystemType == null) return false;
		var subsystem = project.FindSubsystem(subsystemType, null, false);
		var position = owner.ComponentBody.Position + new Engine.Vector3({{dx}}f, 1.5f, 0f);
		var velocity = new Engine.Vector3(8f * (float)System.Math.Sign({{dx}}), 4f, 0f);
		int bobberValue = Game.Terrain.MakeBlockValue({{bobberIndex}}, 0, 0);
		int rodValue = Game.Terrain.MakeBlockValue({{rodIndex}}, 0, 0);
		subsystemType.GetMethod("CastFloat").Invoke(subsystem, new object[] { position, velocity, owner, bobberValue, rodValue });
		return true;
		""");
		return result is bool value && value;
	}

	public static async Task<string> GetTerrainFingerprintAsync(GameContext context, Point3 center, int radius)
	{
		object? result = await context.PushCommandAsync($$"""
		FUNC var terrain = Game.GameManager.Project.FindSubsystem<Game.SubsystemTerrain>().Terrain;
		int hash = unchecked((int)2166136261u);
		int cx = {{center.X}};
		int cy = {{center.Y}};
		int cz = {{center.Z}};
		int r = {{radius}};
		for (int x = cx - r; x <= cx + r; x++) {
		    for (int y = cy - r; y <= cy + r; y++) {
		        for (int z = cz - r; z <= cz + r; z++) {
		            hash = unchecked((hash ^ Game.Terrain.ReplaceLight(terrain.GetCellValue(x, y, z), 0)) * 16777619);
		        }
		    }
		}
		return hash.ToString(System.Globalization.CultureInfo.InvariantCulture);
		""");
		return result as string ?? string.Empty;
	}

	public static async Task<string> GetClothesStateAsync(GameContext context, int playerIndex)
	{
		object? result = await context.PushCommandAsync($$"""
		FUNC var players = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>();
		var pd = players.PlayersData.FirstOrDefault(p => p.PlayerIndex == {{playerIndex}});
		var clothing = pd?.ComponentPlayer?.Entity.FindComponent<Game.ComponentClothing>();
		if (clothing == null) return "missing";
		var parts = new System.Collections.Generic.List<string>();
		foreach (var slot in Game.ClothingSlot.ClothingSlots.Values) {
		    var list = clothing.m_clothes[slot];
		    parts.Add(slot.Name + "=" + string.Join(",", list.Select(v => v.ToString(System.Globalization.CultureInfo.InvariantCulture))));
		}
		return string.Join("|", parts);
		""");
		return result as string ?? "missing";
	}

	public static async Task<bool> EquipClothingAsync(GameContext context, int playerIndex, int slotInt, int value)
	{
		object? result = await context.PushCommandAsync($$"""
		FUNC var players = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>();
		var pd = players.PlayersData.FirstOrDefault(p => p.PlayerIndex == {{playerIndex}});
		var clothing = pd?.ComponentPlayer?.Entity.FindComponent<Game.ComponentClothing>();
		if (clothing == null) return "missing";
		int slotIndex = {{slotInt}};
		var target = Game.ClothingSlot.ClothingSlots.Values.FirstOrDefault(s => (int)s == slotIndex);
		if (target == null) return "no-slot";
		var list = clothing.m_clothes[target];
		list.Clear();
		list.Add({{value}});
		clothing.m_clothedTexturesValid = false;
		return "ok";
		""");
		return (result as string) == "ok";
	}

	public static async Task<string> BuildEnchantedClothingAsync(GameContext context, int clothingSlot, int enchantBit)
	{
		object? result = await context.PushCommandAsync($$"""
		FUNC var baseClothing = Game.BlocksManager.Blocks[203] as Game.ClothingBlock;
		if (baseClothing == null) return "-1";
		var target = baseClothing.m_clothingData.Where(kv => kv.Value.Slot == (Game.ClothingSlot){{clothingSlot}});
		var pair = target.FirstOrDefault();
		if (pair.Value == null) return "-1";
		int data = Game.ClothingBlock.SetClothingIndex(0, pair.Key);
		int enabled = {{enchantBit}};
		if ((enabled & 0x1000) != 0) {
			data = Enchantment.EnchantmentManager.ClothingSetDurability(data, true);
		}
		if ((enabled & 0x2000) != 0) {
			data = Enchantment.EnchantmentManager.ClothingSetMending(data, true);
		}
		if ((enabled & 0x4000) != 0) {
			data = Enchantment.EnchantmentManager.HelmetSetRecall(data, true);
		}
		if ((enabled & 0x8000) != 0) {
			data = Enchantment.EnchantmentManager.HelmetSetAutoFeed(data, true);
		}
		return Game.Terrain.MakeBlockValue(Game.BlocksManager.GetBlockIndex<Enchantment.ENClothing.ENClothingBlock>(false, false), 0, data).ToString(System.Globalization.CultureInfo.InvariantCulture);
		""");
		string raw = result as string ?? "-1";
		if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) || parsed <= 0)
		{
			return "-1";
		}
		return parsed.ToString(CultureInfo.InvariantCulture);
	}

	public static async Task<bool> TriggerDeathAsync(GameContext context, int playerIndex)
	{
		object? result = await context.PushCommandAsync($$"""
		FUNC var players = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>();
		var pd = players.PlayersData.FirstOrDefault(p => p.PlayerIndex == {{playerIndex}});
		if (pd?.ComponentPlayer == null) return "missing";
		var health = pd.ComponentPlayer.ComponentHealth;
		health.IsInvulnerable = false;
		health.m_regenerateLifeEnabled = false;
		health.Health = 1f;
		health.m_lastHealth = 1f;
		health.Injure(2f, null, true, "EnchantmentSyncTest_Recall");
		return "done";
		""");
		return (result as string) == "done";
	}

	public static async Task<bool> TriggerMagmaWalkerAsync(GameContext context, Point3 corner, int waterIndex)
	{
		object? result = await context.PushCommandAsync($$"""
		FUNC var project = Game.GameManager.Project;
		var terrain = project.FindSubsystem<Game.SubsystemTerrain>();
		for (int x = 0; x <= 4; x++) {
		    for (int z = 0; z <= 4; z++) {
		        terrain.ChangeCell({{corner.X}} + x, {{corner.Y}}, {{corner.Z}} + z, Game.Terrain.MakeBlockValue({{waterIndex}}));
		    }
		}
		return true;
		""");
		return result is bool value && value;
	}

	public static async Task<bool> ProjectileTestSetupAsync(GameContext context, int playerIndex, int spearValue, float dropZ, float riseY)
	{
		string z = dropZ.ToString("R", CultureInfo.InvariantCulture);
		string y = riseY.ToString("R", CultureInfo.InvariantCulture);
		object? result = await context.PushCommandAsync($$"""
		FUNC var project = Game.GameManager.Project;
		var pd = project.FindSubsystem<Game.SubsystemPlayers>().PlayersData.FirstOrDefault(p => p.PlayerIndex == {{playerIndex}});
		if (pd?.ComponentPlayer == null) return false;
		var owner = pd.ComponentPlayer.Entity.FindComponent<Game.ComponentCreature>();
		if (owner == null) return false;
		Game.Block block = Game.BlocksManager.Blocks[Game.Terrain.ExtractContents({{spearValue}})];
		if (block == null) return false;
		var basePos = owner.ComponentBody.Position;
		var start = basePos + new Engine.Vector3(1f, {{y}}f, 0f);
		var velocity = new Engine.Vector3(0f, 0f, {{z}}f);
		var proj = project.FindSubsystem<Game.SubsystemProjectiles>().AddProjectile({{spearValue}}, start, velocity, Engine.Vector3.Zero, owner);
		return proj != null;
		""");
		return result is bool value && value;
	}

	public static bool PlayerPositionsMatch(string serverState, string clientState, float epsilon)
	{
		if (!TryParsePlayerPosition(serverState, out float sx, out float sy, out float sz)
			|| !TryParsePlayerPosition(clientState, out float cx, out float cy, out float cz))
		{
			return false;
		}
		return Math.Abs(sx - cx) <= epsilon && Math.Abs(sy - cy) <= epsilon && Math.Abs(sz - cz) <= epsilon;
	}

	private static float ConvertToFloat(object value)
	{
		return value switch {
			int i => i,
			double d => (float)d,
			float f => f,
			long l => l,
			_ => -1f
		};
	}

	private static bool TryParsePlayerPosition(string state, out float x, out float y, out float z)
	{
		x = y = z = float.NaN;
		if (state == null)
		{
			return false;
		}
		foreach (string entry in state.Split(';', StringSplitOptions.RemoveEmptyEntries))
		{
			string[] parts = entry.Split('=', 2);
			if (parts.Length != 2)
			{
				continue;
			}
			if (parts[0] == "x" && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float vx))
			{
				x = vx;
			}
			else if (parts[0] == "y" && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float vy))
			{
				y = vy;
			}
			else if (parts[0] == "z" && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float vz))
			{
				z = vz;
			}
		}
		return !float.IsNaN(x) && !float.IsNaN(y) && !float.IsNaN(z);
	}
}