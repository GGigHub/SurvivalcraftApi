using Engine;
using TemplatesDatabase;

namespace Game.Network;

public class PlayerInteractPacket : PlayerPacketBase {
    public override byte ID => (byte)PacketType.PlayerInteract;
    public ComponentPlayer.InteractEvent m_interactEvent;
    public Ray3 m_netInteractRay;
    public TerrainRaycastResult? m_netInteractRaycast;
    public long m_requestId;

    public PlayerInteractPacket() { }
    public PlayerInteractPacket(ComponentPlayer player) {
        m_fromPlayerIndex = player.PlayerData.PlayerIndex;
        m_interactEvent = player.interactEvent.interactEvent;
        m_netInteractRay = player.interactEvent.NetInteractRay;
        m_netInteractRaycast = player.interactEvent.NetPlaceRaycast;
        m_requestId = NetworkMutationRequestTracker.CreateRequestId();
    }
    public override void Serialize(PacketSerializer archive) {
        SerializePlayerId(archive, null);
        archive.Enum(ref m_interactEvent);
        archive.Value(ref m_netInteractRay);
        archive.Value(ref m_netInteractRaycast);
        archive.Value(ref m_requestId);
    }
    public override void Handle(bool isServer) {
        if (!Enum.IsDefined(m_interactEvent)
            || !IsFinite(m_netInteractRay.Direction)
            || !IsFinite(m_netInteractRay.Position)) {
            return;
        }
        if (isServer) {
            if (From == null
                || From.NetworkState != NetworkState.Playing
                || m_requestId <= 0
                || !NetworkMutationRequestTracker.TryAccept(
                    From,
                    NetworkMutationDomain.PlayerInteract,
                    m_requestId)) {
                return;
            }
        }
        if (!TryResolvePlayer(isServer, out PlayerData playerData)) {
            return;
        }
        playerData.ComponentPlayer.AddInteractEvent(m_interactEvent, m_netInteractRay, m_netInteractRaycast);
        QueuePlayerPacket(this, isServer);
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X)
        && float.IsFinite(value.Y)
        && float.IsFinite(value.Z);
}


