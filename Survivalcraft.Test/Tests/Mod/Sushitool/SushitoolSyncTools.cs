using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Engine;
using Xunit;

namespace Survivalcraft.Test;

internal static class SushitoolSyncTools
{
	public static readonly string SushiSyncBoxBlockTypeName = "Sushi.SushiSyncBoxBlock";
	public static readonly string SushiDiggerBlockTypeName = "Sushi.SushiDiggerBlock";
	public static readonly string SushiRailBlockTypeName = "Sushi.SushiRailBlock";

	public static async Task<Point3> GetPlayerCellAsync(GameContext context)
	{
		IFormatProvider invariantCulture = CultureInfo.InvariantCulture;
		DefaultInterpolatedStringHandler handler = new DefaultInterpolatedStringHandler(123, 0, invariantCulture);
		handler.AppendLiteral("FUNC var p = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>().GlobalSpawnPosition + new Engine.Vector3(6f, 2f, 6f); var c = Game.Terrain.ToCell(p); return string.Join(\",\", c.X, c.Y, c.Z);");
		string raw = ((string)(await context.PushCommandAsync(string.Create(invariantCulture, ref handler)))) ?? string.Empty;
		string[] values = raw.Split(',');
		return new Point3(
			int.Parse(values[0], CultureInfo.InvariantCulture),
			int.Parse(values[1], CultureInfo.InvariantCulture),
			int.Parse(values[2], CultureInfo.InvariantCulture));
	}

	public static async Task<bool> IsProjectReadyAsync(GameContext context)
	{
		object result = await context.PushCommandAsync("FUNC return Game.GameManager.Project != null;");
		return result is bool ready && ready;
	}

	public static async Task<string> GetInstallFingerprintAsync(GameContext context)
	{
		return ((string)(await context.PushCommandAsync("FUNC " + GameTools.ReadScript("Sushitool/GetInstallFingerprintCode.cs")))) ?? string.Empty;
	}

	public static async Task<string> PlaceSushiBlockAsync(GameContext context, Point3 point, string blockTypeName, int data)
	{
		IFormatProvider invariantCulture = CultureInfo.InvariantCulture;
		DefaultInterpolatedStringHandler handler = new DefaultInterpolatedStringHandler(226, 5, invariantCulture);
		handler.AppendLiteral("FUNC ");
		handler.AppendLiteral(GameTools.ReadScript("Sushitool/PlaceSushiBlockCode.cs")
			.Replace("{0}", point.X.ToString(CultureInfo.InvariantCulture))
			.Replace("{1}", point.Y.ToString(CultureInfo.InvariantCulture))
			.Replace("{2}", point.Z.ToString(CultureInfo.InvariantCulture))
			.Replace("{3}", blockTypeName)
			.Replace("{4}", data.ToString(CultureInfo.InvariantCulture)));
		return ((string)(await context.PushCommandAsync(string.Create(invariantCulture, ref handler)))) ?? string.Empty;
	}

	public static async Task SpawnSushiTrainAsync(GameContext context, Point3 point)
	{
		IFormatProvider invariantCulture = CultureInfo.InvariantCulture;
		DefaultInterpolatedStringHandler handler = new DefaultInterpolatedStringHandler(226, 3, invariantCulture);
		handler.AppendLiteral("FUNC ");
		handler.AppendLiteral(GameTools.ReadScript("Sushitool/SpawnSushiTrainCode.cs")
			.Replace("{0}", point.X.ToString(CultureInfo.InvariantCulture))
			.Replace("{1}", point.Y.ToString(CultureInfo.InvariantCulture))
			.Replace("{2}", point.Z.ToString(CultureInfo.InvariantCulture)));
		object raw = await context.PushCommandAsync(string.Create(invariantCulture, ref handler));
		Assert.NotEqual("no-project", raw as string);
		Assert.NotEqual("no-server-permission", raw as string);
	}

	public static async Task<string> DriveSushiTrainAsync(GameContext context, int entityId)
	{
		IFormatProvider invariantCulture = CultureInfo.InvariantCulture;
		DefaultInterpolatedStringHandler handler = new DefaultInterpolatedStringHandler(226, 1, invariantCulture);
		handler.AppendLiteral("FUNC ");
		handler.AppendLiteral(GameTools.ReadScript("Sushitool/DriveSushiTrainCode.cs")
			.Replace("{0}", entityId.ToString(CultureInfo.InvariantCulture)));
		return ((string)(await context.PushCommandAsync(string.Create(invariantCulture, ref handler)))) ?? string.Empty;
	}

	public static async Task WaitForBlockEntityAsync(GameContext context, Point3 point)
	{
		if (await BlockEntityExistsAsync(context, point))
		{
			return;
		}
		for (int i = 0; i < 40; i++)
		{
			await Task.Delay(100);
			if (await BlockEntityExistsAsync(context, point))
			{
				return;
			}
		}
		string cellBefore = await ReadCellValueAsync(context, point);
		await ForceBlockEntityCreationAsync(context, point);
		for (int i = 0; i < 60; i++)
		{
			if (await BlockEntityExistsAsync(context, point))
			{
				return;
			}
			await Task.Delay(100);
		}
		string cellInfo = await ReadCellValueAsync(context, point);
		Assert.Fail(
			$"Block entity at {point} was not synchronized to {context.RoleName}. "
			+ $"Terrain cell value at that point: {cellInfo} (was {cellBefore} before re-toggle). "
			+ $"Recent logs:{Environment.NewLine}{context.GetRecentLogText(500)}");
	}

	public static async Task<bool> BlockEntityExistsAsync(GameContext context, Point3 point)
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
		return result is bool exists && exists;
	}

	public static async Task ForceBlockEntityCreationAsync(GameContext context, Point3 point)
	{
		IFormatProvider invariantCulture = CultureInfo.InvariantCulture;
		DefaultInterpolatedStringHandler handler = new DefaultInterpolatedStringHandler(240, 3, invariantCulture);
		handler.AppendLiteral("FUNC var st = Game.GameManager.Project.FindSubsystem<Game.SubsystemTerrain>(); var val = st.Terrain.GetCellValueFast(");
		handler.AppendFormatted(point.X);
		handler.AppendLiteral(", ");
		handler.AppendFormatted(point.Y);
		handler.AppendLiteral(", ");
		handler.AppendFormatted(point.Z);
		handler.AppendLiteral("); st.ChangeCell(");
		handler.AppendFormatted(point.X);
		handler.AppendLiteral(", ");
		handler.AppendFormatted(point.Y);
		handler.AppendLiteral(", ");
		handler.AppendFormatted(point.Z);
		handler.AppendLiteral(", 0, true, null, true); st.ChangeCell(");
		handler.AppendFormatted(point.X);
		handler.AppendLiteral(", ");
		handler.AppendFormatted(point.Y);
		handler.AppendLiteral(", ");
		handler.AppendFormatted(point.Z);
		handler.AppendLiteral(", val, true, null, true); return \"re-toggled\";");
		await context.PushCommandAsync(string.Create(invariantCulture, ref handler));
	}

	public static async Task<string> ReadCellValueAsync(GameContext context, Point3 point)
	{
		IFormatProvider invariantCulture = CultureInfo.InvariantCulture;
		DefaultInterpolatedStringHandler handler = new DefaultInterpolatedStringHandler(220, 3, invariantCulture);
		handler.AppendLiteral("FUNC var st = Game.GameManager.Project.FindSubsystem<Game.SubsystemTerrain>(); var raw = st.Terrain.GetCellValueFast(");
		handler.AppendFormatted(point.X);
		handler.AppendLiteral(", ");
		handler.AppendFormatted(point.Y);
		handler.AppendLiteral(", ");
		handler.AppendFormatted(point.Z);
		handler.AppendLiteral("); return string.Join(\",\", Game.Terrain.ExtractContents(raw), Game.Terrain.ExtractData(raw), Game.BlocksManager.Blocks[Game.Terrain.ExtractContents(raw)].GetType().FullName);");
		return ((string)(await context.PushCommandAsync(string.Create(invariantCulture, ref handler)))) ?? string.Empty;
	}

	public static async Task<string> GetInventoryRegistrationDiagAsync(GameContext context)
	{
		IFormatProvider invariantCulture = CultureInfo.InvariantCulture;
		DefaultInterpolatedStringHandler handler = new DefaultInterpolatedStringHandler(260, 0, invariantCulture);
		handler.AppendLiteral("FUNC var si = Game.GameManager.Project.FindSubsystem<Game.SubsystemInventories>(); var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic; var byId = si.GetType().GetField(\"inventories\", flags)?.GetValue(si) as System.Collections.IDictionary; var sb = new System.Text.StringBuilder(); sb.Append(\"count=\").Append(byId != null ? byId.Count : -1); if (byId != null) { var keys = new System.Collections.Generic.List<int>(); foreach (object k in byId.Keys) keys.Add((int)k); keys.Sort(); foreach (int k in keys) { var inv = byId[k]; string tn = inv != null ? inv.GetType().FullName : \"null\"; if (tn == \"Sushi.SushiSyncInventory\") { sb.Append(\";syncInvId=\").Append(k); } } } return sb.ToString();");
		return ((string)(await context.PushCommandAsync(string.Create(invariantCulture, ref handler)))) ?? string.Empty;
	}

	public static async Task WaitForTerrainSyncAsync(GameContext client, GameContext server, Point3 point)
	{
		for (int i = 0; i < 150; i++)
		{
			string serverCell = await ReadCellValueAsync(server, point);
			string clientCell = await ReadCellValueAsync(client, point);
			bool clientChunkLoaded = await IsClientChunkLoadedAsync(client, point);
			if (clientChunkLoaded && string.Equals(serverCell, clientCell, StringComparison.Ordinal))
			{
				return;
			}
			await Task.Delay(100);
		}
		Assert.Fail(
			$"Client terrain at {point} did not converge to the server. "
			+ $"server[]={await ReadCellValueAsync(server, point)}; client[]={await ReadCellValueAsync(client, point)}; "
			+ $"clientChunkLoaded={await IsClientChunkLoadedAsync(client, point)}. "
			+ $"Recent logs:{Environment.NewLine}{client.GetRecentLogText(120)}");
	}

	public static async Task<bool> IsClientChunkLoadedAsync(GameContext context, Point3 point)
	{
		IFormatProvider invariantCulture = CultureInfo.InvariantCulture;
		DefaultInterpolatedStringHandler handler = new DefaultInterpolatedStringHandler(160, 2, invariantCulture);
		handler.AppendLiteral("FUNC var st = Game.GameManager.Project.FindSubsystem<Game.SubsystemTerrain>(); var c = st.Terrain.GetChunkAtCell(");
		handler.AppendFormatted(point.X);
		handler.AppendLiteral(", ");
		handler.AppendFormatted(point.Z);
		handler.AppendLiteral("); return c != null && c.IsLoaded;");
		object result = await context.PushCommandAsync(string.Create(invariantCulture, ref handler));
		return result is bool loaded && loaded;
	}

	public static async Task WaitForTrainAsync(GameContext context)
	{
		for (int i = 0; i < 100; i++)
		{
			string fingerprint = await GetTrainFingerprintAsync(context);
			if (!string.Equals(fingerprint, "no-trains", StringComparison.Ordinal) && !string.IsNullOrEmpty(fingerprint))
			{
				return;
			}
			await Task.Delay(100);
		}
		Assert.Fail($"{context.RoleName} never received a Sushi train entity.");
	}

	public static async Task<string> GetChannelFingerprintAsync(GameContext context)
	{
		return ((string)(await context.PushCommandAsync("FUNC " + GameTools.ReadScript("Sushitool/GetChannelFingerprintCode.cs")))) ?? string.Empty;
	}

	public static async Task<string> WaitForChannelMatchAsync(GameContext context, string expected, string[] comparedFields)
	{
		string last = string.Empty;
		for (int i = 0; i < 50; i++)
		{
			last = await GetChannelFingerprintAsync(context);
			if (FieldsEqual(expected, last, comparedFields))
			{
				return last;
			}
			await Task.Delay(150);
		}
		return last;
	}

	private static bool FieldsEqual(string left, string right, string[] fields)
	{
		foreach (string field in fields)
		{
			if (!ExtractField(left, field).Equals(ExtractField(right, field), StringComparison.Ordinal))
			{
				return false;
			}
		}
		return true;
	}

	private static string ExtractField(string fingerprint, string field)
	{
		int start = fingerprint.IndexOf(field, StringComparison.Ordinal);
		if (start < 0)
		{
			return string.Empty;
		}
		int next = fingerprint.IndexOf(';', start);
		return next >= 0 ? fingerprint[start..next] : fingerprint[start..];
	}

	public static async Task<string> GetMachineFingerprintAsync(GameContext context, Point3 point)
	{
		IFormatProvider invariantCulture = CultureInfo.InvariantCulture;
		DefaultInterpolatedStringHandler handler = new DefaultInterpolatedStringHandler(220, 3, invariantCulture);
		handler.AppendLiteral("FUNC ");
		handler.AppendLiteral(GameTools.ReadScript("Sushitool/GetMachineFingerprintCode.cs")
			.Replace("{0}", point.X.ToString(CultureInfo.InvariantCulture))
			.Replace("{1}", point.Y.ToString(CultureInfo.InvariantCulture))
			.Replace("{2}", point.Z.ToString(CultureInfo.InvariantCulture)));
		return ((string)(await context.PushCommandAsync(string.Create(invariantCulture, ref handler)))) ?? string.Empty;
	}

	public static async Task<string> GetTrainFingerprintAsync(GameContext context)
	{
		return ((string)(await context.PushCommandAsync("FUNC " + GameTools.ReadScript("Sushitool/GetTrainFingerprintCode.cs")))) ?? string.Empty;
	}

	public static async Task<string> SetMachineWorkStateAsync(GameContext context, Point3 point, bool loopWork, bool startWork)
	{
		IFormatProvider invariantCulture = CultureInfo.InvariantCulture;
		DefaultInterpolatedStringHandler handler = new DefaultInterpolatedStringHandler(226, 5, invariantCulture);
		handler.AppendLiteral("FUNC ");
		handler.AppendLiteral(GameTools.ReadScript("Sushitool/SetMachineStateCode.cs")
			.Replace("{0}", point.X.ToString(CultureInfo.InvariantCulture))
			.Replace("{1}", point.Y.ToString(CultureInfo.InvariantCulture))
			.Replace("{2}", point.Z.ToString(CultureInfo.InvariantCulture))
			.Replace("{3}", loopWork.ToString().ToLowerInvariant())
			.Replace("{4}", startWork.ToString().ToLowerInvariant()));
		return ((string)(await context.PushCommandAsync(string.Create(invariantCulture, ref handler)))) ?? string.Empty;
	}

	public static async Task<string> ResizeChannelInventoryAsync(GameContext context, int channel, int newSlotCount, int seedValue, int seedCount)
	{
		IFormatProvider invariantCulture = CultureInfo.InvariantCulture;
		DefaultInterpolatedStringHandler handler = new DefaultInterpolatedStringHandler(226, 4, invariantCulture);
		handler.AppendLiteral("FUNC ");
		handler.AppendLiteral(GameTools.ReadScript("Sushitool/ResizeChannelInventoryCode.cs")
			.Replace("{0}", channel.ToString(CultureInfo.InvariantCulture))
			.Replace("{1}", newSlotCount.ToString(CultureInfo.InvariantCulture))
			.Replace("{2}", seedValue.ToString(CultureInfo.InvariantCulture))
			.Replace("{3}", seedCount.ToString(CultureInfo.InvariantCulture)));
		return ((string)(await context.PushCommandAsync(string.Create(invariantCulture, ref handler)))) ?? string.Empty;
	}

	public static async Task<string> MutateChannelInventoryAsync(GameContext context, int channel, int slot, int value, int count)
	{
		IFormatProvider invariantCulture = CultureInfo.InvariantCulture;
		DefaultInterpolatedStringHandler handler = new DefaultInterpolatedStringHandler(226, 4, invariantCulture);
		handler.AppendLiteral("FUNC ");
		handler.AppendLiteral(GameTools.ReadScript("Sushitool/MutateChannelInventoryCode.cs")
			.Replace("{0}", channel.ToString(CultureInfo.InvariantCulture))
			.Replace("{1}", slot.ToString(CultureInfo.InvariantCulture))
			.Replace("{2}", value.ToString(CultureInfo.InvariantCulture))
			.Replace("{3}", count.ToString(CultureInfo.InvariantCulture)));
		return ((string)(await context.PushCommandAsync(string.Create(invariantCulture, ref handler)))) ?? string.Empty;
	}

	public static async Task<string> SendMachineControlRequestAsync(GameContext context, int playerIndex, int entityId, bool enabled)
	{
		IFormatProvider invariantCulture = CultureInfo.InvariantCulture;
		DefaultInterpolatedStringHandler handler = new DefaultInterpolatedStringHandler(226, 3, invariantCulture);
		handler.AppendLiteral("FUNC ");
		handler.AppendLiteral(GameTools.ReadScript("Sushitool/SendMachineControlRequestCode.cs")
			.Replace("{0}", playerIndex.ToString(CultureInfo.InvariantCulture))
			.Replace("{1}", entityId.ToString(CultureInfo.InvariantCulture))
			.Replace("{2}", enabled.ToString().ToLowerInvariant()));
		return ((string)(await context.PushCommandAsync(string.Create(invariantCulture, ref handler)))) ?? string.Empty;
	}

	public static async Task<string> SendTrainControlRequestAsync(GameContext context, int playerIndex, int entityId, int speedDelta, int turnDelta)
	{
		IFormatProvider invariantCulture = CultureInfo.InvariantCulture;
		DefaultInterpolatedStringHandler handler = new DefaultInterpolatedStringHandler(226, 4, invariantCulture);
		handler.AppendLiteral("FUNC ");
		handler.AppendLiteral(GameTools.ReadScript("Sushitool/SendTrainControlRequestCode.cs")
			.Replace("{0}", playerIndex.ToString(CultureInfo.InvariantCulture))
			.Replace("{1}", entityId.ToString(CultureInfo.InvariantCulture))
			.Replace("{2}", speedDelta.ToString(CultureInfo.InvariantCulture))
			.Replace("{3}", turnDelta.ToString(CultureInfo.InvariantCulture)));
		return ((string)(await context.PushCommandAsync(string.Create(invariantCulture, ref handler)))) ?? string.Empty;
	}

	public static async Task<string> MountClientPlayerOnTrainAsync(GameContext context, int playerIndex, int entityId)
	{
		IFormatProvider invariantCulture = CultureInfo.InvariantCulture;
		DefaultInterpolatedStringHandler handler = new DefaultInterpolatedStringHandler(226, 2, invariantCulture);
		handler.AppendLiteral("FUNC ");
		handler.AppendLiteral(GameTools.ReadScript("Sushitool/MountClientPlayerOnTrainCode.cs")
			.Replace("{0}", playerIndex.ToString(CultureInfo.InvariantCulture))
			.Replace("{1}", entityId.ToString(CultureInfo.InvariantCulture)));
		return ((string)(await context.PushCommandAsync(string.Create(invariantCulture, ref handler)))) ?? string.Empty;
	}

	public static async Task WaitForMachineStartWorkAsync(GameContext context, Point3 point, bool expected, string serverDiagnostic = null)
	{
		for (int i = 0; i < 80; i++)
		{
			string fingerprint = await GetMachineFingerprintAsync(context, point);
			if (fingerprint.Contains("m_startWork=" + expected.ToString().ToLowerInvariant(), StringComparison.Ordinal))
			{
				return;
			}
			await Task.Delay(100);
		}
		Assert.Fail($"{context.RoleName} machine m_startWork was not {(expected ? "enabled" : "disabled")}. Fingerprint: {await GetMachineFingerprintAsync(context, point)}"
			+ (string.IsNullOrEmpty(serverDiagnostic) ? string.Empty : $"; server[]={serverDiagnostic}"));
	}

	public static async Task WaitForTrainRiderAsync(GameContext context, int playerEntityId, string serverDiagnostic = null)
	{
		string expectedSuffix = playerEntityId > 0
			? "rider=" + playerEntityId.ToString(CultureInfo.InvariantCulture)
			: null;
		for (int i = 0; i < 80; i++)
		{
			string fingerprint = await GetTrainFingerprintAsync(context);
			if (expectedSuffix != null
				? fingerprint.Contains(expectedSuffix, StringComparison.Ordinal)
				: HasAnyRider(fingerprint))
			{
				return;
			}
			await Task.Delay(100);
		}
		Assert.Fail($"{context.RoleName} train does not report rider {playerEntityId}. Fingerprint: {await GetTrainFingerprintAsync(context)}"
			+ (string.IsNullOrEmpty(serverDiagnostic) ? string.Empty : $"; server[]={serverDiagnostic}"));
	}

	private static bool HasAnyRider(string fingerprint)
	{
		int start = fingerprint.IndexOf("rider=", StringComparison.Ordinal);
		if (start < 0)
		{
			return false;
		}
		int next = fingerprint.IndexOf('|', start);
		string value = next >= 0 ? fingerprint[start..next] : fingerprint[start..];
		return value != "rider=-1";
	}
}