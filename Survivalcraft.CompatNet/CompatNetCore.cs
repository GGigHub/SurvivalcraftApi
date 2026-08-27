using Engine;
using Game.Network;

namespace Game;

/// <summary>
/// Shared runtime for compatibility adapters.
/// </summary>
public static class CompatNetCore {
    public const byte CurrentProtocolVersion = 2;

    private static bool s_networkReady;

    public static bool NetworkBridgeReady => s_networkReady;

    /// <summary>
    /// Resets the runtime state before the multiplayer packet table is rebuilt.
    /// </summary>
    public static void Reset() {
        s_networkReady = false;
    }

    public static void InitializeNetwork() {
        if (s_networkReady) {
            return;
        }

        PacketManager.RegisterPacket<CompatNetPacket>();
        s_networkReady = true;
    }

    public static bool QueueRequest(
        ComponentPlayer player,
        ushort adapterId,
        ushort operation,
        byte[] payload) {
        if (!s_networkReady
            || !NetworkManager.IsClientRunning
            || player == null
            || payload == null
            || payload.Length > CompatNetPacket.MaximumPayloadBytes) {
            return false;
        }

        NetworkManager.Queue(new CompatNetPacket(
            player,
            adapterId,
            operation,
            payload));
        return true;
    }

    public static bool QueueBroadcast(
        ushort adapterId,
        ushort operation,
        byte[] payload,
        ClientSession to = null,
        ClientSession except = null) {
        if (!s_networkReady
            || !NetworkManager.IsServerRunning
            || payload == null
            || payload.Length > CompatNetPacket.MaximumPayloadBytes) {
            return false;
        }

        NetworkManager.Queue(new CompatNetPacket(
            adapterId,
            operation,
            payload,
            CompatNetPacketKind.Broadcast) {
            To = to,
            Except = except
        });
        return true;
    }

}
