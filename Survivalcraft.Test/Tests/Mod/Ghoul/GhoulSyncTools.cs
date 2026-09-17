using System;
using System.Globalization;
using System.Threading.Tasks;

namespace Survivalcraft.Test;

internal static class GhoulSyncTools
{
	public static async Task<bool> IsProjectReadyAsync(GameContext context)
	{
		object? result = await context.PushCommandAsync("FUNC return Game.GameManager.Project != null;");
		return result is bool ready && ready;
	}

	public static async Task<string> ReadGhoulWorldStateAsync(GameContext context)
	{
		return await ReadFingerprintAsync(context, "Ghoul/GetGhoulWorldStateCode.cs", Array.Empty<(string, string)>());
	}

	public static async Task<string> ReadSAGSubsystemStateAsync(GameContext context)
	{
		return await ReadFingerprintAsync(context, "Ghoul/GetSAGSubsystemStateCode.cs", Array.Empty<(string, string)>());
	}

	public static async Task<string> ReadTartareosityStateAsync(GameContext context)
	{
		return await ReadFingerprintAsync(context, "Ghoul/GetTartareosityStateCode.cs", Array.Empty<(string, string)>());
	}

	public static async Task<string> ReadPlayerSAGComponentStateAsync(GameContext context, int playerIndex)
	{
		return await ReadFingerprintAsync(context, "Ghoul/GetPlayerSAGComponentStateCode.cs", [("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture))]);
	}

	public static async Task<string> ReadClothingMassStateAsync(GameContext context, int playerIndex)
	{
		return await ReadFingerprintAsync(context, "Ghoul/GetClothingMassStateCode.cs", [("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture))]);
	}

	public static async Task<string> ReadPlayerPositionAsync(GameContext context, int playerIndex)
	{
		return await ReadFingerprintAsync(context, "Ghoul/GetPlayerPositionCode.cs", [("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture))]);
	}

	public static async Task<string> ReadSAGhoulCreaturesAsync(GameContext context, string centerX, string centerY, string centerZ, float radius)
	{
		return await ReadFingerprintAsync(context, "Ghoul/GetSAGhoulCreaturesCode.cs",
		[
			("__CENTER_X__", centerX),
			("__CENTER_Y__", centerY),
			("__CENTER_Z__", centerZ),
			("__RADIUS__", radius.ToString("R", CultureInfo.InvariantCulture))
		]);
	}

	public static async Task<string> SetSAGDifficultyAsync(GameContext context, int difficulty)
	{
		return await ReadFingerprintAsync(context, "Ghoul/SetSAGDifficultyCode.cs", [("__DIFFICULTY__", difficulty.ToString(CultureInfo.InvariantCulture))]);
	}

	public static async Task<string> SetPlayerSoulsValueAsync(GameContext context, int playerIndex, float soulsValue)
	{
		return await ReadFingerprintAsync(context, "Ghoul/SetPlayerSoulsValueCode.cs",
		[
			("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture)),
			("__SOULS_VALUE__", soulsValue.ToString("R", CultureInfo.InvariantCulture))
		]);
	}

	public static async Task<string> SpawnSAGhoulAtPositionAsync(GameContext context, string entityName, float x, float y, float z, bool isBoss)
	{
		return await ReadFingerprintAsync(context, "Ghoul/SpawnSAGhoulAtPositionCode.cs",
		[
			("__POS_X__", x.ToString("R", CultureInfo.InvariantCulture)),
			("__POS_Y__", y.ToString("R", CultureInfo.InvariantCulture)),
			("__POS_Z__", z.ToString("R", CultureInfo.InvariantCulture)),
			("__ENTITY_NAME__", entityName),
			("__IS_BOSS__", isBoss ? "true" : "false")
		]);
	}

	public static async Task<string> ReadFingerprintAsync(GameContext context, string scriptPath, (string Placeholder, string Value)[] replacements)
	{
		string script = GameTools.ReadScript(scriptPath);
		foreach ((string placeholder, string value) in replacements)
		{
			script = script.Replace(placeholder, value);
		}
		return (string)(await context.PushCommandAsync("FUNC " + script)) ?? string.Empty;
	}
}