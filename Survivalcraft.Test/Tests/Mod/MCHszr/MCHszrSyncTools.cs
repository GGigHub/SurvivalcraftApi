using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Engine;
using Xunit;

namespace Survivalcraft.Test;

internal static class MCHszrSyncTools {
	public const int IronMacheteContents = 124;

	public const int IronSpearContents = 82;

	public const int IronAxeContents = 37;

	public const int IronHammerContents = 230;

	public static readonly TimeSpan ShortWait = TimeSpan.FromMilliseconds(1200);

	public static readonly TimeSpan ConvergenceWait = TimeSpan.FromMilliseconds(5000);

	public static async Task<bool> IsProjectReadyAsync(GameContext context) {
		object? result = await context.PushCommandAsync("FUNC return Game.GameManager.Project != null;");
		return result is bool ready && ready;
	}

	public static async Task EnsureWorldAndClientAsync(GameContext server, GameContext client) {
		if (!(await IsProjectReadyAsync(server))) {
			await PlayerSyncTools.CreateCreativeTestWorldAsync(server, "mchszr-sync");
		}
		await SetSurvivalModeAsync(server);
		if (!(await IsProjectReadyAsync(client))) {
			await GameTools.ConnectTestServer(client, server.GamePort);
		}
		int playerIndex = await PlayerSyncTools.WaitForClientPlayerIndexAsync(client);
		Assert.True(playerIndex >= 0, "Client player index was not assigned.");
		PlayerRuntimeState state = await PlayerSyncTools.WaitForPlayerEntityAsync(playerIndex: playerIndex, context: server);
		Assert.True(state.Exists && state.HasEntity && state.EntityId > 0, $"Server player entity not ready. {state}");
		await InventoryTestTools.WaitForPlayerInventoryAsync(client, playerIndex);
	}

	public static async Task SetSurvivalModeAsync(GameContext server) {
		await server.PushCommandAsync(
			"EXEC Game.GameManager.Project.FindSubsystem<Game.SubsystemGameInfo>().WorldSettings.GameMode = Game.GameMode.Survival;");
	}

	public static async Task<Point3> GetSpawnCellAsync(GameContext server) {
		string raw = (string?)(await server.PushCommandAsync("FUNC var s = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>().GlobalSpawnPosition; var c = Game.Terrain.ToCell(s); return string.Join(\",\", c.X, c.Y, c.Z);")) ?? "0,0,0";
		string[] parts = raw.Split(',');
		return new Point3(
			int.Parse(parts[0], CultureInfo.InvariantCulture),
			int.Parse(parts[1], CultureInfo.InvariantCulture),
			int.Parse(parts[2], CultureInfo.InvariantCulture));
	}

	public static async Task<int> PrepareMeleeAreaAsync(
		GameContext server,
		Point3 origin,
		Vector3Dto dummyPosition,
		float dummyHealth) {
		string script = GameTools.ReadScript("MCHszr/PrepareMeleeAreaCode.cs")
			.Replace("__X__", origin.X.ToString(CultureInfo.InvariantCulture))
			.Replace("__Y__", origin.Y.ToString(CultureInfo.InvariantCulture))
			.Replace("__Z__", origin.Z.ToString(CultureInfo.InvariantCulture))
			.Replace("__DUMMY_X__", dummyPosition.X.ToString("R", CultureInfo.InvariantCulture))
			.Replace("__DUMMY_Y__", dummyPosition.Y.ToString("R", CultureInfo.InvariantCulture))
			.Replace("__DUMMY_Z__", dummyPosition.Z.ToString("R", CultureInfo.InvariantCulture))
			.Replace("__HEALTH__", dummyHealth.ToString("R", CultureInfo.InvariantCulture));
		object? result = await server.PushCommandAsync("FUNC " + script);
		if (result is int entityId && entityId > 0) {
			return entityId;
		}
		if (result is string text && text != "missing" && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)) {
			return parsed;
		}
		throw new InvalidOperationException($"PrepareMeleeArea failed. Result={result ?? "null"}.");
	}

	public static async Task PositionMainPlayerAsync(GameContext client, Vector3Dto position, bool gravity, bool creativeFly) {
		string script = GameTools.ReadScript("MCHszr/PositionMainPlayerCode.cs")
			.Replace("__X__", position.X.ToString("R", CultureInfo.InvariantCulture))
			.Replace("__Y__", position.Y.ToString("R", CultureInfo.InvariantCulture))
			.Replace("__Z__", position.Z.ToString("R", CultureInfo.InvariantCulture))
			.Replace("__GRAVITY__", gravity ? "1" : "0")
			.Replace("__FLY__", creativeFly ? "1" : "0");
		string result = (string?)(await client.PushCommandAsync("FUNC " + script)) ?? "missing";
		Assert.DoesNotContain("missing", result);
	}

	public static async Task WaitForPlayerStandingAsync(GameContext context, int playerIndex, bool standing) {
		PlayerMCHszrState state = await TestWaiter.UntilAsync(
			() => GetPlayerMCHszrStateAsync(context, playerIndex),
			s => s.Exists && s.Standing == standing,
			ConvergenceWait,
			$"{context.RoleName} player standing did not reach {standing}.");
		Assert.Equal(standing, state.Standing);
	}

	public static async Task WaitForServerPlayerConvergedAsync(GameContext server, int playerIndex, Vector3Dto expected, float maxDistanceSquared) {
		await TestWaiter.UntilAsync(
			() => GetPlayerMCHszrStateAsync(server, playerIndex),
			s => s.Exists && Vector3Dto.DistanceSquared(s.Position, expected) <= maxDistanceSquared,
			ConvergenceWait,
			$"Server player did not converge to {expected}.");
	}

	public static async Task SeedWeaponAsync(GameContext server, int playerIndex, int itemValue, int count) {
		string script = GameTools.ReadScript("MCHszr/SetWeaponCode.cs")
			.Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture))
			.Replace("__ITEM_VALUE__", itemValue.ToString(CultureInfo.InvariantCulture))
			.Replace("__ITEM_COUNT__", count.ToString(CultureInfo.InvariantCulture));
		string result = (string?)(await server.PushCommandAsync("FUNC " + script)) ?? "missing";
		Assert.Equal("seeded", result);
	}

	public static async Task WaitForClientWeaponAsync(GameContext client, int playerIndex, int contents, int count) {
		await TestWaiter.UntilAsync(
			() => InventoryTestTools.GetInventorySnapshotAsync(client, playerIndex),
			snapshot => snapshot.Exists
				&& snapshot.Slots.TryGetValue(0, out InventorySlotSnapshot slot)
				&& slot.Contents == contents
				&& slot.Count >= 1,
			ConvergenceWait,
			$"Client player {playerIndex} did not receive weapon contents {contents} in slot 0.");
		await InventoryTestTools.AssertActiveSlotEventuallyAsync(client, playerIndex, 0);
	}

	public static async Task ForcePoseAsync(GameContext context, int playerIndex, int targetPose) {
		string script = GameTools.ReadScript("MCHszr/ForcePoseCode.cs")
			.Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture))
			.Replace("__TARGET_POSE__", targetPose.ToString(CultureInfo.InvariantCulture));
		object? result = await context.PushCommandAsync("FUNC " + script);
		if (result is string text && (text == "missing" || text == "no-behavior" || text == "no-item" || text == "no-field")) {
			throw new InvalidOperationException($"ForcePose failed on {context.RoleName}. Result={result}.");
		}
	}

	public static async Task<int> GetPoseAsync(GameContext context, int playerIndex) {
		string script = GameTools.ReadScript("MCHszr/GetPoseStateCode.cs")
			.Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture));
		object? result = await context.PushCommandAsync("FUNC " + script);
		if (result is int pose) {
			return pose;
		}
		if (result is string text && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)) {
			return parsed;
		}
		return -1;
	}

	public static async Task<string> TogglePoseRealPathAsync(GameContext client, int playerIndex) {
		string script = GameTools.ReadScript("MCHszr/TogglePoseRealPathCode.cs")
			.Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture));
		return (string?)(await client.PushCommandAsync("FUNC " + script)) ?? "missing";
	}

	public static async Task<CameraRay> BuildMeleeRayAsync(GameContext client, int entityId) {
		string script = GameTools.ReadScript("MCHszr/BuildMeleeRayCode.cs")
			.Replace("__ENTITY_ID__", entityId.ToString(CultureInfo.InvariantCulture));
		string raw = (string?)(await client.PushCommandAsync("FUNC " + script)) ?? "missing";
		Dictionary<string, string> values = ParseKeyValuePairs(raw);
		if (values.Count == 0 || raw == "missing") {
			throw new InvalidOperationException($"BuildMeleeRay failed on {client.RoleName}. Result={raw}.");
		}
		return new CameraRay(
			ParseFloat(values, "px"),
			ParseFloat(values, "py"),
			ParseFloat(values, "pz"),
			ParseFloat(values, "dx"),
			ParseFloat(values, "dy"),
			ParseFloat(values, "dz"),
			raw);
	}

	public static async Task SendHitRayAsync(GameContext client, CameraRay ray) {
		await client.PushCommandAsync(string.Create(
			CultureInfo.InvariantCulture,
			$"HIT_RAY {ray.Px:R} {ray.Py:R} {ray.Pz:R} {ray.Dx:R} {ray.Dy:R} {ray.Dz:R}"));
	}

	public static async Task SendAimRayAsync(GameContext client, CameraRay ray, int frameCount) {
		await client.PushCommandAsync(string.Create(
			CultureInfo.InvariantCulture,
			$"AIM_RAY {ray.Px:R} {ray.Py:R} {ray.Pz:R} {ray.Dx:R} {ray.Dy:R} {ray.Dz:R} {frameCount}"));
	}

	public static async Task<CreatureHealthState> GetCreatureHealthAsync(GameContext context, int entityId) {
		string script = GameTools.ReadScript("MCHszr/GetCreatureHealthCode.cs")
			.Replace("__ENTITY_ID__", entityId.ToString(CultureInfo.InvariantCulture));
		string raw = (string?)(await context.PushCommandAsync("FUNC " + script)) ?? "missing";
		return ParseCreatureHealth(raw);
	}

	public static async Task SetBoatHitpointsAsync(GameContext context, int entityId, float hitpoints) {
		string script = GameTools.ReadScript("MCHszr/SetBoatHitpointsCode.cs")
			.Replace("__ENTITY_ID__", entityId.ToString(CultureInfo.InvariantCulture))
			.Replace("__HITPOINTS__", hitpoints.ToString("R", CultureInfo.InvariantCulture));
		string result = (string?)(await context.PushCommandAsync("FUNC " + script)) ?? "missing";
		Assert.Equal("set", result);
	}

	public static async Task<string> GetChargeStateAsync(GameContext client) {
		return (string?)(await client.PushCommandAsync("FUNC " + GameTools.ReadScript("MCHszr/GetChargeStateCode.cs"))) ?? "missing";
	}

	public static async Task<float> MeasureFpsAsync(GameContext client) {
		string script = "FUNC " + GameTools.ReadScript("MCHszr/GetFrameIndexCode.cs");
		object? r1 = await client.PushCommandAsync(script);
		int frame1 = Convert.ToInt32(r1, CultureInfo.InvariantCulture);
		await Task.Delay(500);
		object? r2 = await client.PushCommandAsync(script);
		int frame2 = Convert.ToInt32(r2, CultureInfo.InvariantCulture);
		return Math.Max(1f, (frame2 - frame1) / 0.5f);
	}

	public static async Task<int> AimFramesForHoldAsync(GameContext client, float seconds) {
		float fps = await MeasureFpsAsync(client);
		return (int)Math.Max(240f, fps * seconds);
	}

	public static async Task PinBoatAsync(GameContext context, int entityId, Vector3Dto position) {
		string script = GameTools.ReadScript("MCHszr/PinBoatCode.cs")
			.Replace("__ENTITY_ID__", entityId.ToString(CultureInfo.InvariantCulture))
			.Replace("__X__", position.X.ToString("R", CultureInfo.InvariantCulture))
			.Replace("__Y__", position.Y.ToString("R", CultureInfo.InvariantCulture))
			.Replace("__Z__", position.Z.ToString("R", CultureInfo.InvariantCulture));
		string result = (string?)(await context.PushCommandAsync("FUNC " + script)) ?? "missing";
		Assert.Equal("pinned", result);
	}

	public static async Task TickDashAsync(GameContext client) {
		string result = (string?)(await client.PushCommandAsync("FUNC " + GameTools.ReadScript("MCHszr/TickDashCode.cs"))) ?? "missing";
		Assert.Equal("ticked", result);
	}

	public static async Task ClearAimAsync(GameContext client, Vector3Dto origin) {
		await SendAimRayAsync(client, new CameraRay(origin.X, origin.Y, origin.Z, 0f, 0f, 1f, "clear"), 1);
		await Task.Delay(200);
	}

	public static async Task<PlayerMCHszrState> GetPlayerMCHszrStateAsync(GameContext context, int playerIndex) {
		string script = GameTools.ReadScript("MCHszr/GetPlayerMCHszrStateCode.cs")
			.Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture));
		string raw = (string?)(await context.PushCommandAsync("FUNC " + script)) ?? "missing";
		return ParsePlayerMCHszrState(raw);
	}

	public static async Task SetPlayerVitalsAsync(GameContext server, int playerIndex, float health, float food) {
		string script = GameTools.ReadScript("MCHszr/SetPlayerVitalsCode.cs")
			.Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture))
			.Replace("__HEALTH__", health.ToString("R", CultureInfo.InvariantCulture))
			.Replace("__FOOD__", food.ToString("R", CultureInfo.InvariantCulture));
		string result = (string?)(await server.PushCommandAsync("FUNC " + script)) ?? "missing";
		Assert.Equal("set", result);
	}

	public static async Task<string> GetProjectileFingerprintAsync(GameContext context, int playerIndex, float radius) {
		string script = GameTools.ReadScript("MCHszr/GetProjectileFingerprintCode.cs")
			.Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture))
			.Replace("__RADIUS__", radius.ToString("R", CultureInfo.InvariantCulture));
		return (string?)(await context.PushCommandAsync("FUNC " + script)) ?? "missing";
	}

	public static async Task<string> GetModLoaderStateAsync(GameContext context) {
		return (string?)(await context.PushCommandAsync("FUNC " + GameTools.ReadScript("MCHszr/GetModLoaderStateCode.cs"))) ?? "missing";
	}

	public static async Task<string> GetHitCooldownsAsync(GameContext context) {
		return (string?)(await context.PushCommandAsync("FUNC " + GameTools.ReadScript("MCHszr/GetHitCooldownsCode.cs"))) ?? "missing";
	}

	public static async Task<string> TriggerAimAsync(GameContext client, bool aiming) {
		string script = GameTools.ReadScript("MCHszr/TriggerAimCode.cs")
			.Replace("__AIMING__", aiming ? "true" : "false");
		return (string?)(await client.PushCommandAsync("FUNC " + script)) ?? "missing";
	}

	public static async Task<string> GetWeaponActionDiagnosticAsync(GameContext context, int playerIndex) {
		string script = GameTools.ReadScript("MCHszr/GetWeaponActionDiagnosticCode.cs")
			.Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture));
		return (string?)(await context.PushCommandAsync("FUNC " + script)) ?? "missing";
	}

	public static async Task<string> GetHuixueHealthStateAsync(GameContext context, int playerIndex) {
		string script = GameTools.ReadScript("MCHszr/GetHuixueHealthStateCode.cs")
			.Replace("__PLAYER_INDEX__", playerIndex.ToString(CultureInfo.InvariantCulture));
		return (string?)(await context.PushCommandAsync("FUNC " + script)) ?? "missing";
	}

	public static CreatureHealthState ParseCreatureHealth(string raw) {
		Dictionary<string, string> values = ParseKeyValuePairs(raw);
		if (values.Count == 0 || raw == "missing") {
			return new CreatureHealthState(false, -1f, -1f, -1f, default, default, raw);
		}
		return new CreatureHealthState(
			true,
			ParseFloat(values, "health"),
			ParseFloat(values, "hitpoints"),
			ParseFloat(values, "stun"),
			new Vector3Dto(ParseFloat(values, "px"), ParseFloat(values, "py"), ParseFloat(values, "pz")),
			new Vector3Dto(ParseFloat(values, "vx"), ParseFloat(values, "vy"), ParseFloat(values, "vz")),
			raw);
	}

	public static PlayerMCHszrState ParsePlayerMCHszrState(string raw) {
		Dictionary<string, string> values = ParseKeyValuePairs(raw);
		if (values.Count == 0 || raw == "missing") {
			return new PlayerMCHszrState(false, -1f, -1f, -1f, -1f, -1f, -1f, false, default, default, raw);
		}
		return new PlayerMCHszrState(
			true,
			ParseFloat(values, "walk"),
			ParseFloat(values, "jump"),
			ParseFloat(values, "health"),
			ParseFloat(values, "food"),
			ParseFloat(values, "stamina"),
			ParseFloat(values, "heal"),
			ParseBool(values, "stand"),
			new Vector3Dto(ParseFloat(values, "px"), ParseFloat(values, "py"), ParseFloat(values, "pz")),
			new Vector3Dto(ParseFloat(values, "vx"), ParseFloat(values, "vy"), ParseFloat(values, "vz")),
			raw);
	}

	private static Dictionary<string, string> ParseKeyValuePairs(string value) =>
		value
			.Split(';', StringSplitOptions.RemoveEmptyEntries)
			.Select(p => p.Split('=', 2))
			.Where(p => p.Length == 2)
			.ToDictionary(p => p[0], p => p[1], StringComparer.OrdinalIgnoreCase);

	private static float ParseFloat(Dictionary<string, string> values, string key) =>
		values.TryGetValue(key, out string? value)
		&& float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result)
			? result
			: 0f;

	private static bool ParseBool(Dictionary<string, string> values, string key) =>
		values.TryGetValue(key, out string? value)
		&& bool.TryParse(value, out bool result)
		&& result;
}

public readonly record struct PlayerMCHszrState(
	bool Exists,
	float Walk,
	float Jump,
	float Health,
	float Food,
	float Stamina,
	float Heal,
	bool Standing,
	Vector3Dto Position,
	Vector3Dto Velocity,
	string Raw) {
	public override string ToString() =>
		string.Create(CultureInfo.InvariantCulture, $"(Exists={Exists}, Walk={Walk:R}, Jump={Jump:R}, Health={Health:R}, Food={Food:R}, Stamina={Stamina:R}, Heal={Heal:R}, Standing={Standing}, Pos={Position}, Vel={Velocity}, Raw='{Raw}')");
}

public readonly record struct CreatureHealthState(
	bool Exists,
	float Health,
	float Hitpoints,
	float Stun,
	Vector3Dto Position,
	Vector3Dto Velocity,
	string Raw) {
	public override string ToString() =>
		string.Create(CultureInfo.InvariantCulture, $"(Exists={Exists}, Health={Health:R}, Hitpoints={Hitpoints:R}, Stun={Stun:R}, Pos={Position}, Vel={Velocity}, Raw='{Raw}')");
}