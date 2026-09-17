using System;
using System.IO;

namespace Survivalcraft.Test;

public sealed class IndustriaGameFixture : GameFixture
{
	private const string RecipaediaExModPathEnvironmentVariable = "SURVIVALCRAFT_RECIPAEDIAEX_MOD_PATH";

	private const string IndustriaModPathEnvironmentVariable = "SURVIVALCRAFT_INDUSTRIA_MOD_PATH";

	protected override void ConfigureContext(GameContext context)
	{
		context.InstallMod(FindModPackage(RecipaediaExModPathEnvironmentVariable, "pulled_mods/fixed_2.3_originals/RecipaediaEX_v2.0.0.1.scmod"));
		context.InstallMod(FindModPackage(IndustriaModPathEnvironmentVariable, "pulled_mods/industria_v1.0.0.2.scmod"));
		context.IgnoreErrorLogSubstring("SubsystemUpdate: Updateable");
	}

	private static string FindModPackage(string environmentVariable, string defaultRelativePath)
	{
		string environmentVariable2 = Environment.GetEnvironmentVariable(environmentVariable);
		if (!string.IsNullOrWhiteSpace(environmentVariable2))
		{
			return environmentVariable2;
		}
		string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
		return Path.Combine(desktopPath, "N", defaultRelativePath);
	}
}