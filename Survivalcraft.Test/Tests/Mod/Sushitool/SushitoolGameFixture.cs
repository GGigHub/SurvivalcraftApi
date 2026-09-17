using System;
using System.IO;

namespace Survivalcraft.Test;

public sealed class SushitoolGameFixture : GameFixture
{
	private const string SushiBaseModPathEnvironmentVariable = "SURVIVALCRAFT_SUSHIBASE_MOD_PATH";
	private const string SushiToolModPathEnvironmentVariable = "SURVIVALCRAFT_SUSHITOOL_MOD_PATH";
	private const string SushiTouchModPathEnvironmentVariable = "SURVIVALCRAFT_SUSHITOUCH_MOD_PATH";

	protected override void ConfigureContext(GameContext context)
	{
		context.InstallMod(FindSushiBasePackage(), "00_SushiBase.scmod");
		context.InstallMod(FindSushiToolPackage(), "01_SushiTool.scmod");
		context.InstallMod(FindSushiTouchPackage(), "02_SushiTouch.scmod");
	}

	private static string FindSushiBasePackage()
		=> FindModPackage(SushiBaseModPathEnvironmentVariable, "linglan_fuzhu_v3.0.4.scmod");

	private static string FindSushiToolPackage()
		=> FindModPackage(SushiToolModPathEnvironmentVariable, "linglan_keji_v3.0.4.scmod");

	private static string FindSushiTouchPackage()
		=> FindModPackage(SushiTouchModPathEnvironmentVariable, "linglan_chukong_v2.1.scmod");

	private static string FindModPackage(string environmentVariable, string fileName)
	{
		string configured = Environment.GetEnvironmentVariable(environmentVariable);
		if (!string.IsNullOrWhiteSpace(configured))
		{
			return configured;
		}
		return Path.Combine(@"C:\Users\tk34\Desktop\N\pulled_mods", fileName);
	}
}