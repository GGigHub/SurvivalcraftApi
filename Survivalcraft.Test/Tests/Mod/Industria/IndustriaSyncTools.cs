using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Engine;
using Xunit;

namespace Survivalcraft.Test;

internal static class IndustriaSyncTools
{
	public static async Task<bool> IsProjectReadyAsync(GameContext context)
	{
		object result = await context.PushCommandAsync("FUNC return Game.GameManager.Project != null;");
		return result is bool && (bool)result;
	}

	public static async Task<Point3> GetPlayerCellAsync(GameContext context)
	{
		string[] values = (((string)(await context.PushCommandAsync("FUNC var p = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>().GlobalSpawnPosition + new Engine.Vector3(6f, 2f, 6f); var c = Game.Terrain.ToCell(p); return string.Join(\",\", c.X, c.Y, c.Z);"))) ?? string.Empty).Split(',');
		return new Point3(int.Parse(values[0], CultureInfo.InvariantCulture), int.Parse(values[1], CultureInfo.InvariantCulture), int.Parse(values[2], CultureInfo.InvariantCulture));
	}

	public static async Task<string> GetModsStateAsync(GameContext context)
	{
		return await ExecuteScriptAsync(context, "Industria/GetModsStateCode.cs");
	}

	public static async Task<string> PlaceIndustriaSetupAsync(GameContext context, Point3 origin)
	{
		return await ExecuteScriptAsync(context, "Industria/PlaceIndustriaSetupCode.cs", origin.X, origin.Y, origin.Z);
	}

	public static async Task<string> GetVoltNetStateAsync(GameContext context)
	{
		return await ExecuteScriptAsync(context, "Industria/GetVoltNetStateCode.cs");
	}

	public static async Task<string> GetGalvanicCellStateAsync(GameContext context, Point3 position)
	{
		return await ExecuteScriptAsync(context, "Industria/GetGalvanicCellStateCode.cs", position.X, position.Y, position.Z);
	}

	public static async Task<string> GetElectricFurnaceStateAsync(GameContext context, Point3 position)
	{
		return await ExecuteScriptAsync(context, "Industria/GetElectricFurnaceStateCode.cs", position.X, position.Y, position.Z);
	}

	public static async Task<string> AddElectricFurnaceOreAsync(GameContext context, Point3 position)
	{
		return await ExecuteScriptAsync(context, "Industria/AddElectricFurnaceOreCode.cs", position.X, position.Y, position.Z);
	}

	public static async Task<string> GetFluidTankStateAsync(GameContext context, Point3 position)
	{
		return await ExecuteScriptAsync(context, "Industria/GetFluidTankStateCode.cs", position.X, position.Y, position.Z);
	}

	public static async Task<string> AddFluidTankWaterAsync(GameContext context, Point3 position, float volume)
	{
		return await ExecuteScriptAsync(context, "Industria/AddFluidTankWaterCode.cs", position.X, position.Y, position.Z, volume.ToString("R", CultureInfo.InvariantCulture));
	}

	public static async Task WaitForBlockEntityAsync(GameContext context, Point3 position)
	{
		for (int i = 0; i < 100; i++)
		{
			string result = await ExecuteScriptAsync(context, "Industria/IsBlockEntityPresentCode.cs", position.X, position.Y, position.Z);
			if (string.Equals(result, "True", StringComparison.OrdinalIgnoreCase))
			{
				return;
			}
			await Task.Delay(100);
		}
		Assert.Fail($"Block entity at {position} was not synchronized to {context.RoleName}.");
	}

	public static async Task<string> GetTerrainFingerprintAsync(GameContext context, Point3 origin, int minX, int minY, int minZ, int maxX, int maxY, int maxZ)
	{
		IFormatProvider invariantCulture = CultureInfo.InvariantCulture;
		DefaultInterpolatedStringHandler handler = new DefaultInterpolatedStringHandler(432, 6, invariantCulture);
		handler.AppendLiteral("FUNC var terrain = Game.GameManager.Project.FindSubsystem<Game.SubsystemTerrain>().Terrain;\nint hash = unchecked((int)2166136261u);\nfor (int x = ");
		handler.AppendFormatted(origin.X + minX);
		handler.AppendLiteral("; x <= ");
		handler.AppendFormatted(origin.X + maxX);
		handler.AppendLiteral("; x++) {\n    for (int y = ");
		handler.AppendFormatted(origin.Y + minY);
		handler.AppendLiteral("; y <= ");
		handler.AppendFormatted(origin.Y + maxY);
		handler.AppendLiteral("; y++) {\n        for (int z = ");
		handler.AppendFormatted(origin.Z + minZ);
		handler.AppendLiteral("; z <= ");
		handler.AppendFormatted(origin.Z + maxZ);
		handler.AppendLiteral("; z++) {\n            hash = unchecked((hash ^ Game.Terrain.ReplaceLight(terrain.GetCellValue(x, y, z), 0)) * 16777619);\n        }\n    }\n}\nreturn hash.ToString(System.Globalization.CultureInfo.InvariantCulture);");
		string script = string.Create(invariantCulture, ref handler);
		return ((string)(await context.PushCommandAsync(script))) ?? string.Empty;
	}

	public static async Task SeedKeyboardMappingsAsync(GameContext context)
	{
		object result = await context.PushCommandAsync("FUNC Game.SettingsManager.KeyboardMappingSettings.SetValue(\"TechTree\", Engine.Input.Key.F);\nGame.SettingsManager.KeyboardMappingSettings.SetValue(\"Recipaedia\", Engine.Input.Key.T);\nreturn Game.SettingsManager.GetKeyboardMapping(\"TechTree\", false) != null && Game.SettingsManager.GetKeyboardMapping(\"Recipaedia\", false) != null;");
		Assert.True(result is bool && (bool)result, $"{context.RoleName} could not register the Industria keyboard mappings.");
	}

	public static async Task EnsureWorldAndClientAsync(GameFixture fixture, string seed)
	{
		if (!(await IsProjectReadyAsync(fixture.Server)))
		{
			await SeedKeyboardMappingsAsync(fixture.Server);
			await PlayerSyncTools.CreateCreativeTestWorldAsync(fixture.Server, seed);
		}
		if (!(await IsProjectReadyAsync(fixture.Client)))
		{
			await SeedKeyboardMappingsAsync(fixture.Client);
			await GameTools.ConnectTestServer(fixture.Client, fixture.Server.GamePort);
		}
		int playerIndex = await PlayerSyncTools.WaitForClientPlayerIndexAsync(fixture.Client);
		await PlayerSyncTools.WaitForPlayerEntityAsync(fixture.Server, playerIndex);
	}

	public static async Task<Point3> SetupMachineBlockAsync(GameFixture fixture)
	{
		await EnsureWorldAndClientAsync(fixture, "industria-sync");
		Point3 origin = await GetPlayerCellAsync(fixture.Client);
		await WaitForClientChunkLoadedAsync(fixture.Client, origin);
		string placeResult = await PlaceIndustriaSetupAsync(fixture.Server, origin);
		Assert.DoesNotContain("no-", placeResult);
		await WaitForClientDeviceCellsAsync(fixture.Client, fixture.Server, origin);
		string ensureResult = await EnsureDeviceBlockEntitiesClientSideAsync(fixture.Client, origin);
		Assert.DoesNotContain("no-", ensureResult);
		Point3 furnace = origin + new Point3(2, 0, 0);
		Point3 tank = origin + new Point3(4, 0, 0);
		await WaitForBlockEntityAsync(fixture.Client, origin);
		await WaitForBlockEntityAsync(fixture.Client, furnace);
		await WaitForBlockEntityAsync(fixture.Client, tank);
		await WaitForBlockEntityAsync(fixture.Server, origin);
		await WaitForBlockEntityAsync(fixture.Server, furnace);
		await WaitForBlockEntityAsync(fixture.Server, tank);
		return origin;
	}

	public static async Task WaitForClientChunkLoadedAsync(GameContext client, Point3 origin)
	{
		for (int i = 0; i < 120; i++)
		{
			if (i % 5 == 0)
			{
				await RequestClientChunkSyncAsync(client, origin);
			}
			string clientCells = await GetDeviceCellsStateAsync(client, origin);
			if (clientCells.StartsWith("loaded=1;", StringComparison.Ordinal))
			{
				return;
			}
			await Task.Delay(100);
		}
		Assert.Fail($"Client chunk at {origin} never loaded. Last state: '{await GetDeviceCellsStateAsync(client, origin)}'.");
	}

	public static async Task WaitForClientDeviceCellsAsync(GameContext client, GameContext server, Point3 origin)
	{
		for (int i = 0; i < 120; i++)
		{
			if (i % 5 == 0)
			{
				await RequestClientChunkSyncAsync(client, origin);
			}
			string serverCells = await GetDeviceCellsStateAsync(server, origin);
			string clientCells = await GetDeviceCellsStateAsync(client, origin);
			if (CellValuesMatch(serverCells, clientCells))
			{
				return;
			}
			await Task.Delay(100);
		}
		Assert.Fail($"Client device cells never converged. server='{await GetDeviceCellsStateAsync(server, origin)}' / client='{await GetDeviceCellsStateAsync(client, origin)}'.");
	}

	private static bool CellValuesMatch(string left, string right)
	{
		int leftIndex = left.IndexOf(';');
		int rightIndex = right.IndexOf(';');
		if (leftIndex < 0 || rightIndex < 0)
		{
			return false;
		}
		return string.Equals(left.Substring(leftIndex), right.Substring(rightIndex), StringComparison.Ordinal);
	}

	public static async Task<string> GetDeviceCellsStateAsync(GameContext context, Point3 origin)
	{
		return await ExecuteScriptAsync(context, "Industria/GetDeviceCellsStateCode.cs", origin.X, origin.Y, origin.Z);
	}

	public static async Task RequestClientChunkSyncAsync(GameContext context, Point3 origin)
	{
		int chunkX = origin.X >> 4;
		int chunkZ = origin.Z >> 4;
		string script = GameTools.ReadScript("Industria/RequestChunkSyncCode.cs")
			.Replace("__CHUNK_X__", chunkX.ToString(CultureInfo.InvariantCulture))
			.Replace("__CHUNK_Z__", chunkZ.ToString(CultureInfo.InvariantCulture));
		await context.PushCommandAsync("FUNC " + script);
	}

	public static async Task<string> EnsureDeviceBlockEntitiesClientSideAsync(GameContext context, Point3 origin)
	{
		return await ExecuteScriptAsync(context, "Industria/EnsureDeviceBlockEntitiesClientSideCode.cs", origin.X, origin.Y, origin.Z);
	}

	public static async Task WaitForFingerprintMatchAsync(GameContext server, GameContext client, Func<Task<string>> readServer, Func<Task<string>> readClient, string description)
	{
		for (int i = 0; i < 120; i++)
		{
			string serverValue = await readServer();
			string clientValue = await readClient();
			if (string.Equals(serverValue, clientValue, StringComparison.Ordinal))
			{
				return;
			}
			await Task.Delay(250);
		}
		Assert.Fail($"{description} never matched. server='{await readServer()}'; client='{await readClient()}'.");
	}

	public static async Task<string> PrepareClientDeviceReadAsync(GameContext client, Point3 origin, Func<Task<string>> read)
	{
		string value = await read();
		if (!value.StartsWith("no-", StringComparison.Ordinal))
		{
			return value;
		}
		await RequestClientChunkSyncAsync(client, origin);
		await EnsureDeviceBlockEntitiesClientSideAsync(client, origin);
		string value2 = await read();
		if (value2.StartsWith("no-", StringComparison.Ordinal))
		{
			await Task.Delay(150);
			await EnsureDeviceBlockEntitiesClientSideAsync(client, origin);
			value2 = await read();
		}
		return value2;
	}

	private static async Task<string> ExecuteScriptAsync(GameContext context, string scriptPath, params object[] args)
	{
		string code = GameTools.ReadScript(scriptPath);
		for (int i = 0; i < args.Length; i++)
		{
			code = code.Replace("@@" + i + "@@", Convert.ToString(args[i], CultureInfo.InvariantCulture), StringComparison.Ordinal);
		}
		return ((string)(await context.PushCommandAsync("FUNC " + code))) ?? string.Empty;
	}
}
