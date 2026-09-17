using System;
using System.IO;

namespace Survivalcraft.Test;

public sealed class ElytronGameFixture : GameFixture {
    private const string ModPathEnvironmentVariable = "SURVIVALCRAFT_ELYTRON_MOD_PATH";
    private const string DefaultModPath = @"C:\Users\tk34\Desktop\N\pulled_mods\elytron_v1.4.scmod";

    protected override void ConfigureContext(GameContext context) {
        context.InstallMod(FindModPackage());
    }

    private static string FindModPackage() {
        string? configuredPath = Environment.GetEnvironmentVariable(ModPathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configuredPath)) {
            return configuredPath;
        }
        return DefaultModPath;
    }
}
