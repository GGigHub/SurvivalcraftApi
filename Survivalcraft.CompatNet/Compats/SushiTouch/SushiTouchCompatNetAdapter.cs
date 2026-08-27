using Engine;
using Game.Network;
using GameEntitySystem;

namespace Game;

/// <summary>
/// Compatibility marker for SushiTouch. SushiTouch only remaps local touch
/// input into the normal ComponentInput path, so no custom network packet is
/// required. Keeping a registered adapter makes package recognition explicit
/// and reserves a stable extension point if a future version adds world logic.
/// </summary>
public sealed class SushiTouchCompatNetAdapter : CompatNetAdapterLoader {
    public const ushort StableAdapterId = 12;
    public const string TargetPackageName = "SushiTouch";
    public const string TargetAssemblyFileName = "SushiTouch.dll";

    public override ushort AdapterId => StableAdapterId;
    public override string AdapterName => TargetPackageName;

    public override void OnPreloadAssemblies(IList<ModAssemblyImage> assemblies) {
        if (assemblies?.Any(image => image?.Owner?.modInfo != null
                && string.Equals(
                    image.Owner.modInfo.PackageName,
                    TargetPackageName,
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    image.FileName,
                    TargetAssemblyFileName,
                    StringComparison.OrdinalIgnoreCase)) == true) {
            Log.Information(
                $"[Survivalcraft.CompatNet] Recognized {TargetPackageName}; standard input synchronization applies.");
        }
    }

    public override void HandleRequest(CompatNetPacket packet, PlayerData playerData) {
        // SushiTouch does not own world, inventory, or entity operations.
    }

    public void HandleBroadcast(CompatNetPacket packet) {
        // SushiTouch does not own world, inventory, or entity operations.
    }
}
