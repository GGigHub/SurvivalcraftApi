using Game.Network;

namespace Game;

/// <summary>
/// Compatibility adapter for the original ysf.neorxna mod.
/// </summary>
public sealed class NeorxnaCompatNetAdapter : CompatNetAdapterLoader, ICompatNetAdapter {
    public const ushort StableAdapterId = 4;
    public const string TargetPackageName = "ysf.neorxna";

    public override ushort AdapterId => StableAdapterId;
    public override string AdapterName => TargetPackageName;

    public override void HandleRequest(CompatNetPacket packet, PlayerData playerData) {
    }
}
