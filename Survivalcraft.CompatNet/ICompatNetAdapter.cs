using Game.Network;
using GameEntitySystem;

namespace Game;

/// <summary>
/// Adapter contract for making one mod's client actions server-authoritative.
/// </summary>
public interface ICompatNetAdapter {
    /// <summary>
    /// Stable ID used inside CompatNetPacket. ID 0 is reserved.
    /// </summary>
    ushort AdapterId { get; }

    string AdapterName { get; }

    /// <summary>
    /// Rewrites target mod assembly images before they enter the CLR.
    /// </summary>
    void OnPreloadAssemblies(IList<ModAssemblyImage> assemblies);

    /// <summary>
    /// Handles a request after the Core has authenticated its player sender.
    /// </summary>
    void HandleRequest(CompatNetPacket packet, PlayerData playerData);

    /// <summary>
    /// Handles a server-to-client state or result packet. Request-only adapters
    /// may keep the default implementation.
    /// </summary>
    void HandleBroadcast(CompatNetPacket packet) {
    }

    /// <summary>
    /// Gives an adapter a post-update server tick for low-frequency,
    /// server-authoritative state broadcasts. The default is intentionally a
    /// no-op so request-only adapters do not need to implement it.
    /// </summary>
    void OnServerUpdate(float dt) {
    }

    /// <summary>
    /// Returns whether an adapter temporarily owns the specified update on the
    /// server. This is used for stateful compatibility commands whose vanilla
    /// update would otherwise advance before the authoritative snapshot is
    /// observed by a client.
    /// </summary>
    bool ShouldSkipServerUpdate(IUpdateable updateable) => false;

    /// <summary>
    /// Returns whether this adapter owns the specified update on a remote
    /// client and the game's local update should be skipped.
    /// </summary>
    bool ShouldSkipClientUpdate(IUpdateable updateable) => false;
}

/// <summary>
/// Base class for an adapter shipped as its own preload mod.
/// </summary>
public abstract class CompatNetAdapterLoader : ModLoader, ICompatNetAdapter {
    public abstract ushort AdapterId { get; }

    public virtual string AdapterName => GetType().FullName ?? GetType().Name;

    public override void __ModInitialize() {
        CompatNetAdapterRegistry.Register(this);
        ModsManager.RegisterHook("OnProjectileAdded", this);
    }

    public override void OnPreloadAssemblies(IList<ModAssemblyImage> assemblies) {
    }

    public abstract void HandleRequest(CompatNetPacket packet, PlayerData playerData);

    public virtual void HandleBroadcast(CompatNetPacket packet) {
    }

    public virtual void OnServerUpdate(float dt) {
    }

    public virtual bool ShouldSkipServerUpdate(IUpdateable updateable) => false;

    public virtual bool ShouldSkipClientUpdate(IUpdateable updateable) => false;
}
