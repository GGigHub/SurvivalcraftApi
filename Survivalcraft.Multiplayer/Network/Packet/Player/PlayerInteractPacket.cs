using Engine;
using TemplatesDatabase;

namespace Game.Network;

public class PlayerInteractPacket : PlayerPacketBase {
    public override byte ID => (byte)PacketType.PlayerInteract;
    public ComponentPlayer.InteractEvent m_interactEvent;
    public Ray3 m_netInteractRay;
    public TerrainRaycastResult? m_netInteractRaycast;

    public PlayerInteractPacket() { }
    public PlayerInteractPacket(ComponentPlayer player) {
        m_fromPlayerIndex = player.PlayerData.PlayerIndex;
        m_interactEvent = player.interactEvent.interactEvent;
        m_netInteractRay = player.interactEvent.NetInteractRay;
        m_netInteractRaycast = player.interactEvent.NetPlaceRaycast;
    }
    public override void Serialize(PacketSerializer archive) {
        SerializePlayerId(archive, archive.IsWriting ? ResolvePlayer(m_fromPlayerIndex) : null);
        archive.Enum(ref m_interactEvent);
        archive.Value(ref m_netInteractRay);
        archive.Value(ref m_netInteractRaycast);
    }
    public override void Handle(bool isServer) {
        if (!TryResolvePlayer(isServer, out PlayerData playerData)) return;
        playerData.ComponentPlayer.AddInteractEvent(m_interactEvent, m_netInteractRay, m_netInteractRaycast);
        QueuePlayerPacket(this, isServer);
    }
}


