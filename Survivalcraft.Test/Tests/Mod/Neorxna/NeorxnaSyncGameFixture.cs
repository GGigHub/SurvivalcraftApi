using System;
using System.IO;

namespace Survivalcraft.Test;

// Installs ysf.neorxna and Survivalcraft.CompatNet into every game process.
// CompatNet and Multiplayer/TestAutomation packages are packed into the test
// build output automatically (see the CompatNet/TestAutomation csproj and
// build/NetworkMod.targets), so only the target mod package is copied here.
public sealed class NeorxnaSyncGameFixture : GameFixture {
    private const string ModPathEnvironmentVariable = "SURVIVALCRAFT_NEORXNA_MOD_PATH";
    private const string DefaultModPath = @"C:\Users\tk34\Desktop\N\pulled_mods\neorxna_v1.4.scmod";
    private const string CompatNetModFileName = "Survivalcraft.CompatNet.scmod";

    protected override void ConfigureContext(GameContext context) {
        context.InstallMod(FindModPackage());
        context.InstallMod(FindCompatNetPackage());
    }

    private static string FindModPackage() {
        string? configuredPath = Environment.GetEnvironmentVariable(ModPathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configuredPath)) {
            return configuredPath;
        }

        return DefaultModPath;
    }

    private static string FindCompatNetPackage() {
        return Path.Combine(AppContext.BaseDirectory, "Mods", CompatNetModFileName);
    }
}
