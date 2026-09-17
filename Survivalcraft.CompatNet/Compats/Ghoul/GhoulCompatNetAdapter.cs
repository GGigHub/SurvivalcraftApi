using System.Reflection;
using Game.Network;
using GameEntitySystem;

namespace Game;

/// <summary>
/// CompatNet adapter for the optional Ghoul mod.
/// </summary>
public sealed class GhoulCompatNetAdapter : CompatNetAdapterLoader {
    public const ushort StableAdapterId = 12;

    private bool m_targetLoaded;

    public override ushort AdapterId => StableAdapterId;
    public override string AdapterName => GhoulCompatNetRuntime.TargetPackageName;

    public override void OnPreloadAssemblies(IList<ModAssemblyImage> assemblies) {
        m_targetLoaded = false;
        GhoulCompatNetRuntime.Reset();
        foreach (ModAssemblyImage image in assemblies ?? Array.Empty<ModAssemblyImage>()) {
            string packageName = image?.Owner?.modInfo?.PackageName;
            string fileName = image?.FileName;
            if (string.Equals(packageName, GhoulCompatNetRuntime.TargetPackageName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(fileName, GhoulCompatNetRuntime.TargetAssemblyName, StringComparison.OrdinalIgnoreCase)) {
                m_targetLoaded = true;
            }
        }
    }

    public override void HandleRequest(CompatNetPacket packet, PlayerData playerData) {
        // Ghoul mutations are currently corrected by the authoritative snapshot.
    }

    public override void HandleBroadcast(CompatNetPacket packet) {
        if ((m_targetLoaded || GhoulCompatNetRuntime.IsAvailable())
            && packet?.Operation == (ushort)GhoulCompatNetOperation.StateSnapshot
            && NetworkManager.IsClientRunning
            && !NetworkManager.IsServerRunning) {
            GhoulCompatNetRuntime.Apply(packet.Payload);
        }
    }

    public override void OnServerUpdate(float dt) {
        if (m_targetLoaded || GhoulCompatNetRuntime.IsAvailable()) {
            GhoulCompatNetRuntime.Publish();
        }
    }

    public override bool ShouldSkipClientUpdate(IUpdateable updateable) {
        if (!(m_targetLoaded || GhoulCompatNetRuntime.IsAvailable())
            || !NetworkManager.IsClientRunning
            || NetworkManager.IsServerRunning
            || updateable == null) {
            return false;
        }

        Assembly assembly = updateable.GetType().Assembly;
        string simpleName = assembly.GetName().Name ?? string.Empty;
        string moduleName = assembly.ManifestModule?.Name ?? string.Empty;
        return simpleName.Contains("saghoul", StringComparison.OrdinalIgnoreCase)
            || moduleName.Contains("saghoul", StringComparison.OrdinalIgnoreCase)
            || updateable.GetType().FullName?.StartsWith("SAGhoul.", StringComparison.Ordinal) == true;
    }
}
