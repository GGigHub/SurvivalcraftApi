using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace Survivalcraft.Test;

public sealed class CommandBlockGameFixture : GameFixture
{
	private const string ModPathEnvironmentVariable = "SURVIVALCRAFT_COMMANDBLOCK_MOD_PATH";

	private const string ModFileName = "01803_[API1.9]命令方块4.1.9.scmod";

	protected override void ConfigureContext(GameContext context)
	{
		context.InstallMod(FindModPackage());
	}

	private static string FindModPackage()
	{
		string environmentVariable = Environment.GetEnvironmentVariable("SURVIVALCRAFT_COMMANDBLOCK_MOD_PATH");
		if (!string.IsNullOrWhiteSpace(environmentVariable))
		{
			return environmentVariable;
		}
		InlineArray5<string> buffer = default(InlineArray5<string>);
		buffer[0] = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
		buffer[1] = "NF3";
		buffer[2] = "tools";
		buffer[3] = "CommunityMods";
		buffer[4] = "01803_[API1.9]命令方块4.1.9.scmod";
		return Path.Combine(buffer);
	}
}


