using Engine;
using GameEntitySystem;

namespace Game.Network;

/// <summary>
/// The single wire-level packet used by all CompatNet adapters.
/// </summary>
public sealed class CompatNetPacket : PlayerPacketBase {
    public const int MaximumPayloadBytes = 64 * 1024;

    public override byte ID => (byte)PacketType.ModPacket;

    public byte ProtocolVersion = CompatNetCore.CurrentProtocolVersion;
    public CompatNetPacketKind Kind;
    public ushort AdapterId;
    public ushort Operation;
    public byte[] Payload = [];

    public CompatNetPacket() {
    }

    public CompatNetPacket(
        ComponentPlayer player,
        ushort adapterId,
        ushort operation,
        byte[] payload) {
        m_fromPlayerIndex = player?.PlayerData?.PlayerIndex ?? -1;
        AdapterId = adapterId;
        Operation = operation;
        Payload = payload ?? [];
    }

    public CompatNetPacket(
        ushort adapterId,
        ushort operation,
        byte[] payload,
        CompatNetPacketKind kind) {
        Kind = kind;
        AdapterId = adapterId;
        Operation = operation;
        Payload = payload ?? [];
    }

    public override void Serialize(PacketSerializer archive) {
        SerializePlayerId(archive, null);
        archive.Value(ref ProtocolVersion);
        archive.Enum(ref Kind);
        archive.Value(ref AdapterId);
        archive.Value(ref Operation);
        archive.Buffer(ref Payload, MaximumPayloadBytes);
    }

    public override void Handle(bool isServer) {
        if (ProtocolVersion != CompatNetCore.CurrentProtocolVersion
            || !CompatNetAdapterRegistry.TryGet(AdapterId, out ICompatNetAdapter adapter)) {
            return;
        }

        try {
            if (Kind == CompatNetPacketKind.Request) {
                if (!isServer || !TryResolvePlayer(true, out PlayerData playerData)) {
                    return;
                }
                adapter.HandleRequest(this, playerData);
            }
            else if (Kind == CompatNetPacketKind.Broadcast && !isServer) {
                adapter.HandleBroadcast(this);
            }
        }
        catch (Exception e) {
            // Adapter payloads are mod-defined. A malformed request must not
            // escape the network receive callback and take down the server.
            Log.Error($"[Survivalcraft.CompatNet] Adapter {adapter.AdapterName} failed to handle request: {e}");
        }
    }
}
