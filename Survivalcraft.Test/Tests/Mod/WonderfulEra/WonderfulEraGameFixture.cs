using System;
using System.IO;

namespace Survivalcraft.Test;

// WonderfulEra and CompatNet are installed into both game processes.
// The fixture mirrors NeorxnaGameFixture: the WonderfulEra scmod comes from a
// configurable path (default: the pulled_mods folder), while CompatNet is
// expected to be already generated into the test output Mods folder by the
// Survivalcraft.CompatNet project.
public sealed class WonderfulEraGameFixture : GameFixture {
    private const string ModPathEnvironmentVariable = "SURVIVALCRAFT_WONDERFULERA_MOD_PATH";
    private const string DefaultModFileName = "wonderfulera_v1.9.3a.scmod";
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

        return Path.Combine(
            @"C:\Users\tk34\Desktop\N\pulled_mods",
            DefaultModFileName);
    }

    private static string FindCompatNetPackage() {
        return Path.Combine(AppContext.BaseDirectory, "Mods", CompatNetModFileName);
    }
}
