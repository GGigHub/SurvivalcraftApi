using System.Reflection;
using Engine;
using Game.Network;
using GameEntitySystem;

namespace Game;

public enum ScopedUpdateGuardScope {
    RemotePlayerOnly,
    RemoteEntityBody,
    AllNonPlayerEntities
}

/// <summary>
/// Conservative CompatNet baseline for a mod whose client-side component updates
/// must remain authoritative for server-owned entities.
/// </summary>
/// <remarks>
/// The adapter deliberately identifies target components by their loaded
/// assembly name instead of referencing any type from the target mod. This keeps
/// the compatibility layer loadable when the optional mod is not installed.
/// </remarks>
public sealed class ScopedUpdateGuardCompatNetAdapter : ICompatNetAdapter {
    public const ushort EnchantmentAdapterId = 5;
    public const ushort MchszrAdapterId = 6;
    public const ushort SushiToolAdapterId = 8;
    public const ushort SubnauticaAdapterId = 9;
    public const ushort WonderfulEraAdapterId = 10;
    public const ushort IndustrialEra2AdapterId = 11;

    private volatile bool m_targetPackageLoaded;
    private double m_nextStatePublishTime;
    private double m_nextFullRefreshTime;
    private int m_lastAppliedStateSequence;
    private double m_nextSushiInventoryPublishTime;
    private double m_nextSushiInventoryFullRefreshTime;
    private int m_sushiInventorySequence;
    private int m_lastAppliedSushiInventorySequence;
    private double m_nextIndustrialTankPublishTime;
    private double m_nextIndustrialTankFullRefreshTime;
    private int m_industrialTankSequence;
    private int m_lastAppliedIndustrialTankSequence;
    private double m_nextSubnauticaPublishTime;
    private double m_nextSubnauticaFullRefreshTime;
    private int m_subnauticaSequence;
    private int m_lastAppliedSubnauticaSequence;
    private readonly Dictionary<int, byte[]> m_lastSushiInventoryPayloads = [];
    private readonly Dictionary<string, byte[]> m_lastIndustrialTankPayloads = [];
    private readonly Dictionary<int, byte[]> m_lastSubnauticaPayloads = [];
    private readonly Dictionary<string, byte[]> m_lastStatePayloads = [];

    public ScopedUpdateGuardCompatNetAdapter(
        ushort stableId,
        string packageName,
        string assemblyName,
        ScopedUpdateGuardScope scope = ScopedUpdateGuardScope.RemotePlayerOnly) {
        if (stableId == 0) {
            throw new ArgumentOutOfRangeException(nameof(stableId), "Adapter ID 0 is reserved.");
        }

        if (string.IsNullOrWhiteSpace(packageName)) {
            throw new ArgumentException("A package name is required.", nameof(packageName));
        }

        if (string.IsNullOrWhiteSpace(assemblyName)) {
            throw new ArgumentException("An assembly name is required.", nameof(assemblyName));
        }

        AdapterId = stableId;
        PackageName = packageName;
        AssemblyName = assemblyName;
        Scope = scope;
    }

    public ushort AdapterId { get; }

    public string AdapterName => PackageName;

    public string PackageName { get; }

    public string AssemblyName { get; }

    public ScopedUpdateGuardScope Scope { get; }

    /// <summary>
    /// Indicates that a loaded mod image declared the configured package name.
    /// This is reset on every preload pass so state cannot leak between loads.
    /// </summary>
    public bool IsTargetPackageLoaded => m_targetPackageLoaded;

    /// <summary>
    /// Creates a configurable adapter without requiring a reference to the
    /// optional target mod.
    /// </summary>
    public static ScopedUpdateGuardCompatNetAdapter Create(
        ushort stableId,
        string packageName,
        string assemblyName,
        ScopedUpdateGuardScope scope = ScopedUpdateGuardScope.RemotePlayerOnly) {
        return new ScopedUpdateGuardCompatNetAdapter(
            stableId,
            packageName,
            assemblyName,
            scope);
    }

    /// <summary>
    /// The built-in baseline definitions for the supported optional mods.
    /// </summary>
    public static readonly ScopedUpdateGuardCompatNetAdapter[] Presets = [
        new(EnchantmentAdapterId, "Enchantment", "Enchantment.dll"),
        new(MchszrAdapterId, "MCHszr", "MCHszr.dll"),
        new(SushiToolAdapterId, "SushiTool", "SushiTool.dll", ScopedUpdateGuardScope.AllNonPlayerEntities),
        new(SubnauticaAdapterId, "Tp.Subnautica", "sc-subnautica-mod.dll", ScopedUpdateGuardScope.RemoteEntityBody),
        new(WonderfulEraAdapterId, "Tp.WonderfulEra", "WonderfulEra.dll", ScopedUpdateGuardScope.RemoteEntityBody),
        new(IndustrialEra2AdapterId, "com.IndustrialEra2", "SCIENEW.dll", ScopedUpdateGuardScope.AllNonPlayerEntities)
    ];

    /// <summary>
    /// Returns fresh adapter instances suitable for registration by a loader.
    /// Fresh instances ensure preload state is not shared between registrations.
    /// </summary>
    public static ScopedUpdateGuardCompatNetAdapter[] CreatePresets() {
        return Presets
            .Select(adapter => new ScopedUpdateGuardCompatNetAdapter(
                adapter.AdapterId,
                adapter.PackageName,
                adapter.AssemblyName,
                adapter.Scope))
            .ToArray();
    }

    public void OnPreloadAssemblies(IList<ModAssemblyImage> assemblies) {
        m_targetPackageLoaded = false;
        m_nextStatePublishTime = 0;
        m_nextFullRefreshTime = 0;
        m_lastAppliedStateSequence = 0;
        m_nextSushiInventoryPublishTime = 0;
        m_nextSushiInventoryFullRefreshTime = 0;
        m_sushiInventorySequence = 0;
        m_lastAppliedSushiInventorySequence = 0;
        m_nextIndustrialTankPublishTime = 0;
        m_nextIndustrialTankFullRefreshTime = 0;
        m_industrialTankSequence = 0;
        m_lastAppliedIndustrialTankSequence = 0;
        m_nextSubnauticaPublishTime = 0;
        m_nextSubnauticaFullRefreshTime = 0;
        m_subnauticaSequence = 0;
        m_lastAppliedSubnauticaSequence = 0;
        m_lastSushiInventoryPayloads.Clear();
        m_lastIndustrialTankPayloads.Clear();
        m_lastSubnauticaPayloads.Clear();
        m_lastStatePayloads.Clear();
        SushiToolCompatNetRuntime.Reset();
        SubnauticaCompatNetRuntime.Reset();
        WonderfulEraCompatNetRuntime.Reset();

        foreach (ModAssemblyImage image in assemblies ?? Array.Empty<ModAssemblyImage>()) {
            if (image?.Owner?.modInfo != null
                && string.Equals(
                    image.Owner.modInfo.PackageName,
                    PackageName,
                    StringComparison.OrdinalIgnoreCase)) {
                m_targetPackageLoaded = true;
                if (AdapterId == SushiToolAdapterId) {
                    try {
                        if (SushiToolCompatNetPatcher.Patch(image)) {
                            Log.Information(
                                $"[Survivalcraft.CompatNet] Patched {image.FileName} in memory.");
                        }
                    }
                    catch (Exception e) {
                        Log.Error(
                            $"[Survivalcraft.CompatNet] Failed to patch {image.FileName}: {e}");
                    }
                }
                else if (AdapterId == SubnauticaAdapterId) {
                    try {
                        if (SubnauticaCompatNetPatcher.Patch(image)) {
                            Log.Information(
                                $"[Survivalcraft.CompatNet] Patched {image.FileName} in memory.");
                        }
                    }
                    catch (Exception e) {
                        Log.Error(
                            $"[Survivalcraft.CompatNet] Failed to patch {image.FileName}: {e}");
                    }
                }
                else if (AdapterId == WonderfulEraAdapterId) {
                    try {
                        if (WonderfulEraCompatNetPatcher.Patch(image)) {
                            Log.Information(
                                $"[Survivalcraft.CompatNet] Patched {image.FileName} in memory.");
                        }
                    }
                    catch (Exception e) {
                        Log.Error(
                            $"[Survivalcraft.CompatNet] Failed to patch {image.FileName}: {e}");
                    }
                }
            }
        }
    }

    public bool ShouldSkipClientUpdate(IUpdateable updateable) {
        if (!m_targetPackageLoaded
            || !NetworkManager.IsClientRunning
            || NetworkManager.IsServerRunning
            || updateable == null
            || !IsTargetAssembly(updateable.GetType().Assembly)) {
            return false;
        }

        // Industrial Era 2 simulates shared fluid and electric networks in
        // updateable subsystems. Leaving those ticks enabled would let every
        // client mutate the same tank graph independently of the server. The
        // two listed subsystems only calculate/debug client presentation and
        // are therefore intentionally left local.
        if (AdapterId == IndustrialEra2AdapterId && updateable is Subsystem) {
            string subsystemName = updateable.GetType().Name;
            return !string.Equals(
                       subsystemName,
                       "SubsystemIEBenchmarkDisplay",
                       StringComparison.Ordinal)
                && !string.Equals(
                       subsystemName,
                       "SubsystemTerrainFracDrawer",
                       StringComparison.Ordinal);
        }

        if (AdapterId == SushiToolAdapterId
            && SushiToolCompatNetRuntime.IsLocallyRiddenTrain(updateable)) {
            return false;
        }

        if (updateable is not Component component || component.Entity == null) {
            return false;
        }

        ComponentPlayer player = component.Entity.FindComponent<ComponentPlayer>();
        if (player?.PlayerData != null) {
            return !player.PlayerData.IsMainPlayer;
        }

        return Scope switch {
            ScopedUpdateGuardScope.RemoteEntityBody =>
                component.Entity.FindComponent<ComponentBody>() != null,
            ScopedUpdateGuardScope.AllNonPlayerEntities => true,
            _ => false
        };
    }

    /// <summary>
    /// SushiTool request operations are handled by its optional runtime bridge.
    /// Unknown operations remain intentionally ignored.
    /// </summary>
    public void HandleRequest(CompatNetPacket packet, PlayerData playerData) {
        if (AdapterId == SushiToolAdapterId
            && m_targetPackageLoaded
            && (packet?.Operation == (ushort)SushiToolCompatNetOperation.AddChannelRequest
                || packet?.Operation == (ushort)SushiToolCompatNetOperation.InventoryActionRequest
                || packet?.Operation == (ushort)SushiToolCompatNetOperation.ChannelMetadataRequest
                || packet?.Operation == (ushort)SushiToolCompatNetOperation.MachineControlRequest
                || packet?.Operation == (ushort)SushiToolCompatNetOperation.MachineConfigRequest
                || packet?.Operation == (ushort)SushiToolCompatNetOperation.TrainControlRequest
                || packet?.Operation == (ushort)SushiToolCompatNetOperation.TrainRecycleRequest
                || packet?.Operation == (ushort)SushiToolCompatNetOperation.TrainCallRequest
                || packet?.Operation == (ushort)SushiToolCompatNetOperation.TrainMountRequest)) {
            SushiToolCompatNetRuntime.HandleRequest(packet, playerData);
        }
        else if (AdapterId == SubnauticaAdapterId
            && m_targetPackageLoaded
            && (packet?.Operation == (ushort)SubnauticaCompatNetOperation.MermaidFeedRequest
                || packet?.Operation == (ushort)SubnauticaCompatNetOperation.MermaidQuestAcceptRequest
                || packet?.Operation == (ushort)SubnauticaCompatNetOperation.MermaidQuestDeliverRequest)) {
            SubnauticaCompatNetRuntime.HandleRequest(packet, playerData);
        }
        else if (AdapterId == WonderfulEraAdapterId
            && m_targetPackageLoaded
            && packet?.Operation == (ushort)WonderfulEraCompatNetOperation.VehicleControlRequest) {
            WonderfulEraCompatNetRuntime.HandleRequest(packet, playerData);
        }
    }

    /// <summary>
    /// This baseline adapter emits no broadcasts and safely ignores any packet.
    /// </summary>
    public void HandleBroadcast(CompatNetPacket packet) {
        if (packet?.Operation == (ushort)CompatNetStateOperation.StateSnapshot
            && m_targetPackageLoaded
            && NetworkManager.IsClientRunning
            && !NetworkManager.IsServerRunning) {
            CompatNetStateRuntime.Apply(
                packet.Payload,
                AssemblyName,
                Scope,
                ref m_lastAppliedStateSequence);
        }
        else if (AdapterId == SushiToolAdapterId
            && packet?.Operation == (ushort)SushiToolCompatNetOperation.ChannelStateSnapshot
            && m_targetPackageLoaded
            && NetworkManager.IsClientRunning
            && !NetworkManager.IsServerRunning) {
            SushiToolCompatNetRuntime.Apply(
                packet.Payload,
                ref m_lastAppliedSushiInventorySequence);
        }
        else if (AdapterId == SushiToolAdapterId
            && packet?.Operation == (ushort)SushiToolCompatNetOperation.TrainCallEvent
            && m_targetPackageLoaded
            && NetworkManager.IsClientRunning
            && !NetworkManager.IsServerRunning) {
            SushiToolCompatNetRuntime.HandleBroadcast(packet);
        }
        else if (AdapterId == IndustrialEra2AdapterId
            && packet?.Operation == (ushort)IndustrialEra2CompatNetOperation.TankStateSnapshot
            && m_targetPackageLoaded
            && NetworkManager.IsClientRunning
            && !NetworkManager.IsServerRunning) {
            IndustrialEra2CompatNetRuntime.Apply(
                packet.Payload,
                ref m_lastAppliedIndustrialTankSequence);
        }
        else if (AdapterId == SubnauticaAdapterId
            && packet?.Operation == (ushort)SubnauticaCompatNetOperation.MermaidStateSnapshot
            && m_targetPackageLoaded
            && NetworkManager.IsClientRunning
            && !NetworkManager.IsServerRunning) {
            SubnauticaCompatNetRuntime.Apply(
                packet.Payload,
                ref m_lastAppliedSubnauticaSequence);
        }
    }

    public void OnServerUpdate(float dt) {
        CompatNetStateRuntime.Publish(
            AdapterId,
            AssemblyName,
            Scope,
            m_targetPackageLoaded,
            ref m_nextStatePublishTime,
            ref m_nextFullRefreshTime,
            m_lastStatePayloads);

        if (AdapterId == SushiToolAdapterId && m_targetPackageLoaded) {
            SushiToolCompatNetRuntime.Publish(
                ref m_nextSushiInventoryPublishTime,
                ref m_nextSushiInventoryFullRefreshTime,
                ref m_sushiInventorySequence,
                m_lastSushiInventoryPayloads);
        }

        if (AdapterId == IndustrialEra2AdapterId && m_targetPackageLoaded) {
            IndustrialEra2CompatNetRuntime.Publish(
                ref m_nextIndustrialTankPublishTime,
                ref m_nextIndustrialTankFullRefreshTime,
                ref m_industrialTankSequence,
                m_lastIndustrialTankPayloads);
        }

        if (AdapterId == SubnauticaAdapterId && m_targetPackageLoaded) {
            SubnauticaCompatNetRuntime.Publish(
                ref m_nextSubnauticaPublishTime,
                ref m_nextSubnauticaFullRefreshTime,
                ref m_subnauticaSequence,
                m_lastSubnauticaPayloads);
        }
    }

    private bool IsTargetAssembly(Assembly assembly) {
        if (assembly == null) {
            return false;
        }

        string simpleName = assembly.GetName().Name;
        string moduleName = assembly.ManifestModule?.Name;
        return AssemblyNamesEqual(simpleName, AssemblyName)
            || AssemblyNamesEqual(moduleName, AssemblyName);
    }

    private static bool AssemblyNamesEqual(string left, string right) {
        return string.Equals(
            NormalizeAssemblyName(left),
            NormalizeAssemblyName(right),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeAssemblyName(string name) {
        if (string.IsNullOrEmpty(name)) {
            return string.Empty;
        }

        string fileName = name.Replace('\\', '/');
        int separator = fileName.LastIndexOf('/');
        if (separator >= 0) {
            fileName = fileName[(separator + 1)..];
        }

        return fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            ? fileName[..^4]
            : fileName;
    }
}
