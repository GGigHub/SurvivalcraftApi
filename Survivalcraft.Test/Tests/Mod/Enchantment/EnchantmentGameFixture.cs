using System;
using System.IO;

namespace Survivalcraft.Test;

public sealed class EnchantmentGameFixture : GameFixture
{
	private const string ModPathEnvironmentVariable = "SURVIVALCRAFT_ENCHANTMENT_MOD_PATH";

	private const string DefaultModPath = @"C:\Users\tk34\Desktop\N\pulled_mods\enchantment_v1.3.5.scmod";

	protected override void ConfigureContext(GameContext context)
	{
		context.InstallMod(FindModPackage());
	}

	private static string FindModPackage()
	{
		string environmentVariable = Environment.GetEnvironmentVariable(ModPathEnvironmentVariable);
		if (!string.IsNullOrWhiteSpace(environmentVariable))
		{
			return environmentVariable;
		}
		return Path.GetFullPath(DefaultModPath);
	}
}