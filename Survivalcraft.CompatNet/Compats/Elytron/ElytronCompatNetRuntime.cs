using Engine;
using Game.Network;
using GameEntitySystem;

namespace Game;

/// <summary>
/// Runtime guard for the Cecil-patched Elytron assembly. Elytron remains an
/// optional dependency: target objects are accessed through object/reflection.
/// </summary>
public static class ElytronCompatNetRuntime {
    /// <summary>
    /// Existing remote-update guard. A client never simulates Elytron's local
    /// input/physics update for another player's entity, so a remote player's
    /// gliding state cannot be triggered locally. The remote body is driven by
    /// the authoritative PlayerBodyUpdatePacket relayed by the server.
    /// </summary>
    public static bool ShouldSkipGlidingUpdate(object glidingComponent) {
        if (!IsClientOnly()
            || glidingComponent is not Component component
            || component.Entity == null) {
            return false;
        }

        ComponentPlayer player = component.Entity.FindComponent<ComponentPlayer>();
        return player?.PlayerData != null && !player.PlayerData.IsMainPlayer;
    }

    private static bool IsClientOnly() {
        return NetworkManager.IsClientRunning && !NetworkManager.IsServerRunning;
    }
}