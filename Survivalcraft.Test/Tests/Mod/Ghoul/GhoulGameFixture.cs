using System;
using System.IO;

namespace Survivalcraft.Test;

public sealed class GhoulGameFixture : GameFixture
{
	private const string ModPathEnvironmentVariable = "SURVIVALCRAFT_GHOUL_MOD_PATH";

	private const string DefaultModFilePath = @"C:\Users\tk34\Desktop\N\pulled_mods\ghoul_v2.0.scmod";

	protected override void ConfigureContext(GameContext context)
	{
		context.InstallMod(FindModPackage());
	}

	private static string FindModPackage()
	{
		string? configuredPath = Environment.GetEnvironmentVariable(ModPathEnvironmentVariable);
		if (!string.IsNullOrWhiteSpace(configuredPath))
		{
			return configuredPath;
		}
		return DefaultModFilePath;
	}
}