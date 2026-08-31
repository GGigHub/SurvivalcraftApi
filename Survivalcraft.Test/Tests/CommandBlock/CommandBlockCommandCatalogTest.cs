using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using Xunit;

namespace Survivalcraft.Test;

public sealed class CommandBlockCommandCatalogTest
{
	[Fact]
	public void InstructionDocumentContainsTheRequestedCommandSurface()
	{
		string text = Environment.GetEnvironmentVariable("SURVIVALCRAFT_COMMANDBLOCK_MOD_PATH");
		if (text == null)
		{
			InlineArray5<string> buffer = default(InlineArray5<string>);
			buffer[0] = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
			buffer[1] = "NF3";
			buffer[2] = "tools";
			buffer[3] = "CommunityMods";
			buffer[4] = "01803_[API1.9]命令方块4.1.9.scmod";
			text = Path.Combine(buffer);
		}
		string text2 = text;
		Assert.True(File.Exists(text2), "Command block package not found: " + text2);
		using ZipArchive zipArchive = ZipFile.OpenRead(text2);
		ZipArchiveEntry zipArchiveEntry = zipArchive.GetEntry("Assets/InstructionsDoc_Def.txt") ?? zipArchive.GetEntry("Assets/InstructionsDoc_Def.xml");
		Assert.NotNull(zipArchiveEntry);
		using StreamReader streamReader = new StreamReader(zipArchiveEntry.Open());
		string actualString = streamReader.ReadToEnd();
		string[] array = new string[43]
		{
			"place", "replace", "copyblock", "moveblock", "moveset", "addnpc", "removenpc", "injure", "heal", "catchfire",
			"teleport", "spawn", "stats", "action", "additem", "removeitem", "clearitem", "weather", "skycolor", "temperature",
			"humidity", "gametime", "gamemode", "camera", "music", "audio", "image", "pattern", "build", "cmdtext",
			"if:blockexist", "if:entityexist", "if:dropexist", "if:itemexist", "if:handitem", "if:actionmake", "if:signtext", "if:clickinteract", "if:longpress", "if:timerange",
			"if:patternbutton", "if:moveset", "if:musicplay"
		};
		string[] array2 = array;
		foreach (string expectedSubstring in array2)
		{
			Assert.Contains(expectedSubstring, actualString, StringComparison.OrdinalIgnoreCase);
		}
		ZipArchiveEntry zipArchiveEntry2 = zipArchive.GetEntry("Assets/InstructionsDoc_Ext.txt") ?? zipArchive.GetEntry("Assets/InstructionsDoc_Ext.xml");
		Assert.NotNull(zipArchiveEntry2);
		using StreamReader streamReader2 = new StreamReader(zipArchiveEntry2.Open());
		string actualString2 = streamReader2.ReadToEnd();
		string[] array3 = new string[3] { "blockdata", "creaturedata", "clothesdata" };
		foreach (string expectedSubstring2 in array3)
		{
			Assert.Contains(expectedSubstring2, actualString2, StringComparison.OrdinalIgnoreCase);
		}
		ZipArchiveEntry entry = zipArchive.GetEntry("Assets/InstructionsDoc_Particle.xml");
		Assert.NotNull(entry);
		using StreamReader streamReader3 = new StreamReader(entry.Open());
		Assert.Contains("particle", streamReader3.ReadToEnd(), StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void DecompiledAssemblyRegistersExtendedCommandSurface()
	{
		string text = Environment.GetEnvironmentVariable("SURVIVALCRAFT_COMMANDBLOCK_DECOMP_PATH");
		if (text == null)
		{
			InlineArray6<string> buffer = default(InlineArray6<string>);
			buffer[0] = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
			buffer[1] = "NF3";
			buffer[2] = "mod-decode";
			buffer[3] = "decomp";
			buffer[4] = "commandblock";
			buffer[5] = "Command";
			text = Path.Combine(buffer);
		}
		string path = text;
		string actualString = File.ReadAllText(Path.Combine(path, "SubsystemCommandExt.cs"));
		string actualString2 = File.ReadAllText(Path.Combine(path, "SubsystemCommandParticle.cs"));
		Assert.Contains("AddFunction(\"blockdata\"", actualString, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("AddFunction(\"creaturedata\"", actualString, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("AddFunction(\"clothesdata\"", actualString, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("AddFunction(\"particle\"", actualString2, StringComparison.OrdinalIgnoreCase);
	}
}


