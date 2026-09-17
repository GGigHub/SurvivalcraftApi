using System;
using System.IO;
using Engine;
using Xunit;

namespace Survivalcraft.Test;

public sealed class SubnauticaTestState {
    // 服务端玩家出生点附近的锚点格（由 Priority 1 在客户端连接前记录）
    public Point3? PlayerCell { get; set; }

    // 在客户端连接前就放在服务端世界里的灵魂火把位置（用于验证 chunk 同步路径）
    public Point3? PreJoinTorchCell { get; set; }

    // 客户端在连接前放置场景结束后，本地 SoulTorchManager 注册表里的雷达数量
    public int PreJoinClientRadarCount { get; set; } = -1;
}

public sealed class SubnauticaGameFixture : GameFixture {
    private const string ModPathEnvironmentVariable = "SURVIVALCRAFT_SUBNAUTICA_MOD_PATH";

    private const string DefaultModFileName = "subnautica_v1.2.2.scmod";

    private const string DefaultModDirectory = @"C:\Users\tk34\Desktop\N\pulled_mods";

    public SubnauticaTestState State { get; } = new();

    protected override void ConfigureContext(GameContext context) {
        context.InstallMod(FindModPackage());
    }

    private static string FindModPackage() {
        string environmentVariable = Environment.GetEnvironmentVariable(ModPathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(environmentVariable)) {
            return environmentVariable;
        }

        return Path.Combine(DefaultModDirectory, DefaultModFileName);
    }
}