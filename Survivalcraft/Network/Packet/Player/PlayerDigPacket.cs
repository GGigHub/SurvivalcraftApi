using Engine;
using TemplatesDatabase;

namespace Game.Network;

public class PlayerDigPacket : PlayerPacketBase {
    public override byte ID => (byte)PacketType.PlayerDig;
    public ComponentPlayer.DigEvent m_digEvent;
    public Ray3? m_netDigRay;
    public TerrainRaycastResult? m_netDigRaycast;
    public PlayerDigPacket() { }
    public PlayerDigPacket(ComponentPlayer player) {
        m_fromPlayerId = player.PlayerData?.ClientId ?? 0;
        m_digEvent = player.digEvent.digEvent;
        m_netDigRay = player.digEvent.NetDigRay;
        m_netDigRaycast = player.digEvent.NetDigRaycast;
    }
    public override void Serialize(PacketSerializer archive) {
        SerializePlayerId(archive, archive.IsWriting ? PlayerPacketHelpers.ResolvePlayer(m_fromPlayerId) : null);
        archive.Enum(ref m_digEvent);
        archive.Value(ref m_netDigRay);
        archive.Value(ref m_netDigRaycast);
    }
    public override void Handle(bool isServer) {
        if (!TryResolvePlayer(isServer, out PlayerData playerData)) return;
        playerData.ComponentPlayer.AddDigEvent(m_digEvent, m_netDigRay, m_netDigRaycast);
        PlayerPacketHelpers.QueuePlayerPacket(this, isServer);
    }
}

