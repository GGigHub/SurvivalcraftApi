using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Engine;
using Xunit;

namespace Survivalcraft.Test;

internal static class CommandBlockSyncTools
{
	public static async Task<CommandResult> ExecuteCommandAsync(GameContext context, Point3 position, string line, bool judge = false)
	{
		string escapedLine = Escape(line);
		IFormatProvider invariantCulture = CultureInfo.InvariantCulture;
		DefaultInterpolatedStringHandler handler = new DefaultInterpolatedStringHandler(1382, 5, invariantCulture);
		handler.AppendLiteral("FUNC var project = Game.GameManager.Project;\nif (project == null) return \"no-project\";\nvar modAssembly = System.AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == \"CommandBlock\");\nvar dataType = modAssembly?.GetType(\"Command.CommandData\");\nvar subsystemType = modAssembly?.GetType(\"Command.SubsystemCommandDef\");\nif (dataType == null || subsystemType == null) return \"no-command-subsystem\";\nvar command = project.FindSubsystem(subsystemType, null, true);\nvar data = System.Activator.CreateInstance(dataType, new object[] { new Engine.Point3(");
		handler.AppendFormatted(position.X);
		handler.AppendLiteral(",");
		handler.AppendFormatted(position.Y);
		handler.AppendLiteral(",");
		handler.AppendFormatted(position.Z);
		handler.AppendLiteral("), \"");
		handler.AppendFormatted(escapedLine);
		handler.AppendLiteral("\" });\nstring parse = (string?)dataType.GetMethod(\"TrySetValue\")?.Invoke(data, null) ?? string.Empty;\nobject result = subsystemType.GetMethod(\"Submit\")?.Invoke(command, new object[] {\n    dataType.GetField(\"Name\")?.GetValue(data) ?? string.Empty,\n    data,\n    ");
		handler.AppendFormatted(judge.ToString().ToLowerInvariant());
		handler.AppendLiteral("\n}) ?? \"NoSubmitMethod\";\nbool valid = (bool?)dataType.GetField(\"Valid\")?.GetValue(data) ?? false;\nbool outOfRange = (bool?)dataType.GetField(\"OutRange\")?.GetValue(data) ?? false;\nstring name = (string?)dataType.GetField(\"Name\")?.GetValue(data) ?? string.Empty;\nstring type = (string?)dataType.GetField(\"Type\")?.GetValue(data) ?? string.Empty;\nreturn string.Format(System.Globalization.CultureInfo.InvariantCulture, \"result={0};parse={1};valid={2};outOfRange={3};name={4};type={5}\", result, parse.Replace(\";\", \",\"), valid, outOfRange, name, type);");
		string script = string.Create(invariantCulture, ref handler);
		string raw = ((string)(await context.PushCommandAsync(script))) ?? string.Empty;
		return new CommandResult(raw);
	}

	public static async Task<Point3> GetPlayerCellAsync(GameContext context)
	{
		string[] values = (((string)(await context.PushCommandAsync("FUNC var p = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>().GlobalSpawnPosition + new Engine.Vector3(6f, 2f, 6f); var c = Game.Terrain.ToCell(p); return string.Join(\",\", c.X, c.Y, c.Z);"))) ?? string.Empty).Split(',');
		return new Point3(int.Parse(values[0], CultureInfo.InvariantCulture), int.Parse(values[1], CultureInfo.InvariantCulture), int.Parse(values[2], CultureInfo.InvariantCulture));
	}

	public static async Task<string> GetCommandDirectoryAsync(GameContext context)
	{
		return ((string)(await context.PushCommandAsync("FUNC var modAssembly = System.AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == \"CommandBlock\"); var dataHandleType = modAssembly?.GetType(\"Command.DataHandle\"); var storageType = System.AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(\"Engine.Storage\")).FirstOrDefault(t => t != null); if (dataHandleType == null || storageType == null) return string.Empty; var virtualPath = (string)dataHandleType.GetMethod(\"GetCommandPath\")!.Invoke(null, null)!; return (string)storageType.GetMethod(\"GetSystemPath\")!.Invoke(null, new object[] { virtualPath })!;"))) ?? string.Empty;
	}

	public static async Task<bool> IsProjectReadyAsync(GameContext context)
	{
		object result = await context.PushCommandAsync("FUNC return Game.GameManager.Project != null;");
		return result is bool && (bool)result;
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

	public static async Task<string> GetMovingBlockStateAsync(GameContext context)
	{
		return ((string)(await context.PushCommandAsync("FUNC var sets = Game.GameManager.Project.FindSubsystem<Game.SubsystemMovingBlocks>().MovingBlockSetEnumerable.ToList(); return string.Join(\";\", sets.Select(s => string.Format(System.Globalization.CultureInfo.InvariantCulture, \"{0}:{1}:{2:R}:{3:R}:{4:R}\", s.Id, s.Stopped, s.Position.X, s.Position.Y, s.Position.Z)).OrderBy(s => s));"))) ?? string.Empty;
	}

	public static async Task<string> GetCreatureFingerprintAsync(GameContext context, Point3 origin)
	{
		IFormatProvider invariantCulture = CultureInfo.InvariantCulture;
		DefaultInterpolatedStringHandler handler = new DefaultInterpolatedStringHandler(781, 3, invariantCulture);
		handler.AppendLiteral("FUNC var project = Game.GameManager.Project;\nvar center = new Engine.Vector3(");
		handler.AppendFormatted((float)origin.X + 0.5f);
		handler.AppendLiteral("f, ");
		handler.AppendFormatted((float)origin.Y + 0.5f);
		handler.AppendLiteral("f, ");
		handler.AppendFormatted((float)origin.Z + 0.5f);
		handler.AppendLiteral("f);\nvar list = project.Entities\n    .Where(e => e.FindComponent<Game.ComponentCreature>() != null && e.FindComponent<Game.ComponentPlayer>() == null)\n    .Select(e => new { Entity = e, Body = e.FindComponent<Game.ComponentBody>(), Health = e.FindComponent<Game.ComponentHealth>(), Fire = e.FindComponent<Game.ComponentOnFire>() })\n    .Where(x => x.Body != null && Engine.Vector3.DistanceSquared(x.Body.Position, center) <= 256f)\n    .Select(x => string.Format(System.Globalization.CultureInfo.InvariantCulture, \"{0}:{1}:{2:R}:{3}\", x.Entity.Id, x.Entity.ValuesDictionary.DatabaseObject.Name, x.Health?.Health ?? -1f, x.Fire?.IsOnFire ?? false))\n    .OrderBy(s => s);\nreturn string.Join(\";\", list);");
		return ((string)(await context.PushCommandAsync(string.Create(invariantCulture, ref handler)))) ?? string.Empty;
	}

	public static async Task<string> GetPlayerInventorySlotAsync(GameContext context, int playerIndex, int slotIndex)
	{
		IFormatProvider invariantCulture = CultureInfo.InvariantCulture;
		DefaultInterpolatedStringHandler handler = new DefaultInterpolatedStringHandler(369, 3, invariantCulture);
		handler.AppendLiteral("FUNC var player = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>().PlayersData.FirstOrDefault(p => p.PlayerIndex == ");
		handler.AppendFormatted(playerIndex);
		handler.AppendLiteral(")?.ComponentPlayer;\nvar inventory = player?.ComponentMiner?.Inventory;\nreturn inventory == null ? \"missing\" : string.Format(System.Globalization.CultureInfo.InvariantCulture, \"{0}:{1}\", inventory.GetSlotValue(");
		handler.AppendFormatted(slotIndex);
		handler.AppendLiteral("), inventory.GetSlotCount(");
		handler.AppendFormatted(slotIndex);
		handler.AppendLiteral("));");
		return ((string)(await context.PushCommandAsync(string.Create(invariantCulture, ref handler)))) ?? string.Empty;
	}

	public static async Task<PlayerCommandState> GetPlayerCommandStateAsync(GameContext context, int playerIndex)
	{
		IFormatProvider invariantCulture = CultureInfo.InvariantCulture;
		DefaultInterpolatedStringHandler handler = new DefaultInterpolatedStringHandler(997, 1, invariantCulture);
		handler.AppendLiteral("FUNC var players = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>();\nvar playerData = players.PlayersData.FirstOrDefault(p => p.PlayerIndex == ");
		handler.AppendFormatted(playerIndex);
		handler.AppendLiteral(");\nvar player = playerData?.ComponentPlayer;\nvar body = player?.ComponentBody;\nvar health = player?.ComponentHealth;\nvar vitalStats = player?.ComponentVitalStats;\nvar locomotion = player?.ComponentLocomotion;\nvar spawn = playerData?.SpawnPosition ?? Engine.Vector3.Zero;\nreturn player == null ? \"missing\" : string.Format(\n    System.Globalization.CultureInfo.InvariantCulture,\n    \"x={0:R};y={1:R};z={2:R};spawnx={3:R};spawny={4:R};spawnz={5:R};health={6:R};food={7:R};walk={8:R};jump={9:R};invulnerable={10};crouch={11}\",\n    body?.Position.X ?? 0f,\n    body?.Position.Y ?? 0f,\n    body?.Position.Z ?? 0f,\n    spawn.X,\n    spawn.Y,\n    spawn.Z,\n    health?.Health ?? -1f,\n    vitalStats?.Food ?? -1f,\n    locomotion?.WalkSpeed ?? -1f,\n    locomotion?.JumpSpeed ?? -1f,\n    health?.IsInvulnerable ?? false,\n    body?.IsCrouching ?? false);");
		string raw = ((string)(await context.PushCommandAsync(string.Create(invariantCulture, ref handler)))) ?? string.Empty;
		return PlayerCommandState.Parse(raw);
	}

	public static async Task CreateContainerAsync(GameContext context, Point3 point, int contents)
	{
		IFormatProvider invariantCulture = CultureInfo.InvariantCulture;
		DefaultInterpolatedStringHandler handler = new DefaultInterpolatedStringHandler(153, 4, invariantCulture);
		handler.AppendLiteral("FUNC var terrain = Game.GameManager.Project.FindSubsystem<Game.SubsystemTerrain>();\nterrain.ChangeCell(");
		handler.AppendFormatted(point.X);
		handler.AppendLiteral(", ");
		handler.AppendFormatted(point.Y);
		handler.AppendLiteral(", ");
		handler.AppendFormatted(point.Z);
		handler.AppendLiteral(", Game.Terrain.MakeBlockValue(");
		handler.AppendFormatted(contents);
		handler.AppendLiteral("));\nreturn true;");
		object result = await context.PushCommandAsync(string.Create(invariantCulture, ref handler));
		Assert.True(result is bool && (bool)result, $"Failed to create container at {point}.");
	}

	public static async Task WaitForContainerAsync(GameContext context, Point3 point)
	{
		for (int i = 0; i < 80; i++)
		{
			IFormatProvider invariantCulture = CultureInfo.InvariantCulture;
			DefaultInterpolatedStringHandler handler = new DefaultInterpolatedStringHandler(111, 3, invariantCulture);
			handler.AppendLiteral("FUNC return Game.GameManager.Project.FindSubsystem<Game.SubsystemBlockEntities>().GetBlockEntity(");
			handler.AppendFormatted(point.X);
			handler.AppendLiteral(", ");
			handler.AppendFormatted(point.Y);
			handler.AppendLiteral(", ");
			handler.AppendFormatted(point.Z);
			handler.AppendLiteral(") != null;");
			object result = await context.PushCommandAsync(string.Create(invariantCulture, ref handler));
			if (result is bool && (bool)result)
			{
				return;
			}
			await Task.Delay(100);
		}
		Assert.Fail($"Container at {point} was not synchronized to {context.RoleName}.");
	}

	public static async Task<string> GetContainerSlotAsync(GameContext context, Point3 point)
	{
		IFormatProvider invariantCulture = CultureInfo.InvariantCulture;
		DefaultInterpolatedStringHandler handler = new DefaultInterpolatedStringHandler(363, 3, invariantCulture);
		handler.AppendLiteral("FUNC var entity = Game.GameManager.Project.FindSubsystem<Game.SubsystemBlockEntities>().GetBlockEntity(");
		handler.AppendFormatted(point.X);
		handler.AppendLiteral(", ");
		handler.AppendFormatted(point.Y);
		handler.AppendLiteral(", ");
		handler.AppendFormatted(point.Z);
		handler.AppendLiteral(")?.Entity;\nvar inventory = entity?.FindComponents<Game.IInventory>().FirstOrDefault();\nreturn inventory == null ? \"missing\" : string.Format(System.Globalization.CultureInfo.InvariantCulture, \"{0}:{1}\", inventory.GetSlotValue(0), inventory.GetSlotCount(0));");
		return ((string)(await context.PushCommandAsync(string.Create(invariantCulture, ref handler)))) ?? string.Empty;
	}

	public static async Task<string> GetWorldStateAsync(GameContext context, Point3 point)
	{
		IFormatProvider invariantCulture = CultureInfo.InvariantCulture;
		DefaultInterpolatedStringHandler handler = new DefaultInterpolatedStringHandler(1283, 6, invariantCulture);
		handler.AppendLiteral("FUNC var project = Game.GameManager.Project;\nvar command = project.Subsystems.FirstOrDefault(s => s.GetType().FullName == \"Command.SubsystemCommandDef\");\nif (command == null) return \"no-command-subsystem\";\nvar skyColor = (Engine.Color)command.GetType().GetField(\"m_skyColor\")!.GetValue(command)!;\nvar rainColor = (Engine.Color)command.GetType().GetField(\"m_rainColor\")!.GetValue(command)!;\nvar weather = project.FindSubsystem<Game.SubsystemWeather>();\nvar time = project.FindSubsystem<Game.SubsystemTime>();\nvar timeOfDay = project.FindSubsystem<Game.SubsystemTimeOfDay>();\nvar info = project.FindSubsystem<Game.SubsystemGameInfo>();\nvar chunk = project.FindSubsystem<Game.SubsystemTerrain>().Terrain.GetChunkAtCell(");
		handler.AppendFormatted(point.X);
		handler.AppendLiteral(", ");
		handler.AppendFormatted(point.Z);
		handler.AppendLiteral(");\nint temperature = chunk?.GetTemperatureFast(");
		handler.AppendFormatted(point.X);
		handler.AppendLiteral(" & 15, ");
		handler.AppendFormatted(point.Z);
		handler.AppendLiteral(" & 15) ?? -1;\nint humidity = chunk?.GetHumidityFast(");
		handler.AppendFormatted(point.X);
		handler.AppendLiteral(" & 15, ");
		handler.AppendFormatted(point.Z);
		handler.AppendLiteral(" & 15) ?? -1;\nreturn string.Format(System.Globalization.CultureInfo.InvariantCulture, \"sky={0};rain={1};factor={2:R};day={3};mode={4};temp={5};humidity={6};precip={7:R}:{8:R};fog={9:R}:{10:R}\", skyColor.PackedValue, rainColor.PackedValue, time.BasicGameTimeFactor, timeOfDay.DayDuration, info.WorldSettings.GameMode, temperature, humidity, weather.m_precipitationStartTime, weather.m_precipitationEndTime, weather.m_fogStartTime, weather.m_fogEndTime);");
		return ((string)(await context.PushCommandAsync(string.Create(invariantCulture, ref handler)))) ?? string.Empty;
	}

	public static async Task WaitUntilContainsAsync(GameContext context, Func<Task<string>> read, string expected)
	{
		for (int i = 0; i < 120; i++)
		{
			if ((await read()).Contains(expected, StringComparison.OrdinalIgnoreCase))
			{
				return;
			}
			await Task.Delay(100);
		}
		Assert.Fail(context.RoleName + " did not expose '" + expected + "'.");
	}

	public static bool PlayerStatesMatch(PlayerRuntimeState server, PlayerRuntimeState client)
	{
		return server.Exists == client.Exists && server.PlayerIndex == client.PlayerIndex && server.HasEntity == client.HasEntity && server.EntityId == client.EntityId && Math.Abs(server.Level - client.Level) <= 0.001f && server.IsCreativeFlyEnabled == client.IsCreativeFlyEnabled && Math.Abs(server.Health - client.Health) <= 0.01f && string.Equals(server.State, client.State, StringComparison.Ordinal);
	}

	private static string Escape(string value)
	{
		return value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal)
			.Replace("\n", "\\n", StringComparison.Ordinal);
	}
}


