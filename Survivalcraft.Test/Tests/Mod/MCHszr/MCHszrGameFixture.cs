using System;
using System.IO;

namespace Survivalcraft.Test;

public sealed class MCHszrGameFixture : GameFixture {
	private const string ModPathEnvironmentVariable = "SURVIVALCRAFT_MCHSZR_MOD_PATH";

	private const string DefaultModPath = @"C:\Users\tk34\Desktop\N\pulled_mods\mchszr_battle_opt_v1.4.scmod";

	protected override void ConfigureContext(GameContext context) {
		context.InstallMod(FindModPackage());
	}

	private static string FindModPackage() {
		string? environmentVariable = Environment.GetEnvironmentVariable(ModPathEnvironmentVariable);
		if (!string.IsNullOrWhiteSpace(environmentVariable)) {
			return environmentVariable;
		}
		return Path.GetFullPath(DefaultModPath);
	}
}