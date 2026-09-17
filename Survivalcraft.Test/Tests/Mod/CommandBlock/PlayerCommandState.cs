using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Survivalcraft.Test;

internal readonly record struct PlayerCommandState(float X, float Y, float Z, float SpawnX, float SpawnY, float SpawnZ, float Health, float Food, float WalkSpeed, float JumpSpeed, bool IsInvulnerable, bool IsCrouching, string Raw)
{
	public static PlayerCommandState Parse(string value)
	{
		if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "missing", StringComparison.OrdinalIgnoreCase))
		{
			return new PlayerCommandState(0f, 0f, 0f, 0f, 0f, 0f, -1f, -1f, -1f, -1f, IsInvulnerable: false, IsCrouching: false, value);
		}
		Dictionary<string, string> values = (from part in value.Split(';', StringSplitOptions.RemoveEmptyEntries)
			select part.Split('=', 2) into part
			where part.Length == 2
			select part).ToDictionary<string[], string, string>((string[] part) => part[0], (string[] part) => part[1], StringComparer.OrdinalIgnoreCase);
		return new PlayerCommandState(GetFloat("x", 0f), GetFloat("y", 0f), GetFloat("z", 0f), GetFloat("spawnx", 0f), GetFloat("spawny", 0f), GetFloat("spawnz", 0f), GetFloat("health", -1f), GetFloat("food", -1f), GetFloat("walk", -1f), GetFloat("jump", -1f), GetBool("invulnerable"), GetBool("crouch"), value);
		bool GetBool(string key)
		{
			string value2;
			bool result = default(bool);
			return (values.TryGetValue(key, out value2) && bool.TryParse(value2, out result)) & result;
		}
		float GetFloat(string key, float defaultValue)
		{
			string value2;
			float result;
			return (values.TryGetValue(key, out value2) && float.TryParse(value2, NumberStyles.Float, CultureInfo.InvariantCulture, out result)) ? result : defaultValue;
		}
	}

	public bool Matches(PlayerCommandState other)
	{
		return Math.Abs(X - other.X) <= 0.5f && Math.Abs(Y - other.Y) <= 0.5f && Math.Abs(Z - other.Z) <= 0.5f && Math.Abs(SpawnX - other.SpawnX) <= 0.05f && Math.Abs(SpawnY - other.SpawnY) <= 0.05f && Math.Abs(SpawnZ - other.SpawnZ) <= 0.05f && Math.Abs(Health - other.Health) <= 0.05f && Math.Abs(Food - other.Food) <= 0.05f && Math.Abs(WalkSpeed - other.WalkSpeed) <= 0.05f && Math.Abs(JumpSpeed - other.JumpSpeed) <= 0.05f && IsInvulnerable == other.IsInvulnerable && IsCrouching == other.IsCrouching;
	}
}


