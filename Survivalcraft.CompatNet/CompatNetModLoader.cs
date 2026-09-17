using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game;

public sealed class CompatNetModLoader : ModLoader {
    private static readonly GunCompatNetAdapter s_gunAdapter = new();
    private static readonly CommandCompatNetAdapter s_commandAdapter = new();
    private static readonly ElytronCompatNetAdapter s_elytronAdapter = new();
    private static readonly EnchantmentCompatNetAdapter s_enchantmentAdapter = new();
    private static readonly MCHszrCompatNetAdapter s_mchszrAdapter = new();
    private static readonly GhoulCompatNetAdapter s_ghoulAdapter = new();
    private static readonly object s_preloadSync = new();

    private static readonly ICompatNetAdapter[] s_builtinAdapters = CreateBuiltInAdapters();

    private static IReadOnlyList<ICompatNetAdapter> BuiltInAdapters => s_builtinAdapters;

    private static ICompatNetAdapter[] CreateBuiltInAdapters() {
        List<ICompatNetAdapter> adapters = [
            s_gunAdapter,
            s_commandAdapter,
            s_elytronAdapter,
            s_enchantmentAdapter,
            s_mchszrAdapter,
            s_ghoulAdapter
        ];
        adapters.AddRange(
            ScopedUpdateGuardCompatNetAdapter.CreatePresets()
                .Where(adapter => adapter.AdapterId != MCHszrCompatNetAdapter.StableAdapterId
                    && adapter.AdapterId != EnchantmentCompatNetAdapter.StableAdapterId));
        return adapters.ToArray();
    }

    public override void __ModInitialize() {
        ModsManager.RegisterHook("OnLoadingFinished", this);
        ModsManager.RegisterHook("OnProjectileAdded", this);
        ModsManager.RegisterHook("OnNetworkPlayerStateChanged", this);
        ModsManager.RegisterHook("OnIUpdateableUpdate", this);
        ModsManager.RegisterHook("SubsystemUpdate", this);
        ModsManager.RegisterHook("OnShowDialog", this);
        foreach (ICompatNetAdapter adapter in BuiltInAdapters) {
            CompatNetAdapterRegistry.Register(adapter);
        }
    }

    public override void OnShowDialog(ref ContainerWidget parentWidget, ref Dialog dialog) {
        NeorxnaCompatNetRuntime.FixRecipeDialogParent(ref parentWidget, dialog);
    }

    public override void OnPreloadAssemblies(IList<ModAssemblyImage> assemblies) {
        lock (s_preloadSync) {
            // All preload loaders have been instantiated by this point. Rebuild
            // the registry so adapters added by separate compatibility mods are
            // included, while still keeping the built-in adapters available to
            // direct/unit-test callers.
            CompatNetCore.Reset();
            CommandCompatNetRuntime.Reset();
            CompatNetAdapterRegistry.Reset();
            foreach (ICompatNetAdapter adapter in BuiltInAdapters) {
                CompatNetAdapterRegistry.Register(adapter);
            }
            foreach (ICompatNetAdapter adapter in ModsManager.ModLoaders.OfType<ICompatNetAdapter>()) {
                CompatNetAdapterRegistry.Register(adapter);
            }

            foreach (ICompatNetAdapter adapter in CompatNetAdapterRegistry.Adapters) {
                adapter.OnPreloadAssemblies(assemblies);
            }

            foreach (ModAssemblyImage image in assemblies ?? Array.Empty<ModAssemblyImage>()) {
                if (string.Equals(
                        image?.Owner?.modInfo?.PackageName,
                        "network",
                        StringComparison.OrdinalIgnoreCase)) {
                    try {
                        if (SushiToolCompatNetPatcher.PatchNetworkMod(image)) {
                            Log.Information(
                                $"[Survivalcraft.CompatNet] Patched network inventory hooks in {image.FileName}.");
                        }
                    }
                    catch (Exception e) {
                        Log.Error(
                            $"[Survivalcraft.CompatNet] Failed to patch network inventory hooks in {image.FileName}: {e}");
                    }
                }
            }
        }
    }

    public override void OnLoadingFinished(List<Action> actions) {
        try {
            CompatNetRuntime.InitializeNetwork();
            Log.Information("[Survivalcraft.CompatNet] Registered CompatNetPacket.");
        }
        catch (Exception e) {
            Log.Error($"[Survivalcraft.CompatNet] Failed to register network packet: {e}");
        }
    }

    public override void OnProjectileAdded(
        SubsystemProjectiles subsystemProjectiles,
        ref Projectile projectile,
        ValuesDictionary loadValuesDictionary) {
        s_gunAdapter.OnProjectileAdded(
            subsystemProjectiles,
            ref projectile,
            loadValuesDictionary);
    }

    public override void OnNetworkPlayerStateChanged(NetworkState state) {
        if (state != NetworkState.ProjectLoaded
            || !NetworkManager.IsClientRunning
            || NetworkManager.IsServerRunning) {
            return;
        }

        CommandCompatNetRuntime.ResetPlayerStateSequences();
        ComponentPlayer player = GameManager.Project?
            .FindSubsystem<SubsystemPlayers>()?
            .MainPlayer;
        CommandCompatNetRuntime.RequestElectricSnapshot(player);
    }

    public override void OnIUpdateableUpdate(
        SubsystemUpdate subsystemUpdate,
        IUpdateable updateable,
        float dt,
        out bool skip) {
        skip = false;
        if (NetworkManager.IsServerRunning) {
            foreach (ICompatNetAdapter adapter in CompatNetAdapterRegistry.Adapters) {
                if (adapter.ShouldSkipServerUpdate(updateable)) {
                    skip = true;
                    return;
                }
            }
            return;
        }
        if (!NetworkManager.IsClientRunning) {
            return;
        }

        MCHszrCompatNetRuntime.OnClientUpdate();

        foreach (ICompatNetAdapter adapter in CompatNetAdapterRegistry.Adapters) {
            if (adapter.ShouldSkipClientUpdate(updateable)) {
                skip = true;
                return;
            }
        }
    }

    public override void SubsystemUpdate(SubsystemUpdate subsystemUpdate, float dt) {
        if (!NetworkManager.IsServerRunning) {
            return;
        }

        foreach (ICompatNetAdapter adapter in CompatNetAdapterRegistry.Adapters) {
            try {
                adapter.OnServerUpdate(dt);
            }
            catch (Exception e) {
                Log.Error(
                    $"[Survivalcraft.CompatNet] Server state update failed for {adapter.AdapterName}: {e}");
            }
        }
    }
}
