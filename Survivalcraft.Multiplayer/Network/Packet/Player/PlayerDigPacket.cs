using Engine;
using TemplatesDatabase;

namespace Game.Network;

public class PlayerDigPacket : PlayerPacketBase {
    public override byte ID => (byte)PacketType.PlayerDig;
    public ComponentPlayer.DigEvent m_digEvent;
    public Ray3? m_netDigRay;
    public TerrainRaycastResult? m_netDigRaycast;
    public bool m_hasDigTarget;
    public CellFace m_digTarget;
    public int m_digOldValue;
    public int m_digExpectedValue;
    public PlayerDigPacket() { }
    public PlayerDigPacket(ComponentPlayer player) {
        m_fromPlayerIndex = player.PlayerData.PlayerIndex;
        m_digEvent = player.digEvent.digEvent;
        m_netDigRay = player.digEvent.NetDigRay;
        m_netDigRaycast = player.digEvent.NetDigRaycast;
        m_hasDigTarget = player.digEvent.HasDigTarget;
        m_digTarget = player.digEvent.DigTarget;
        m_digOldValue = player.digEvent.DigOldValue;
        m_digExpectedValue = player.digEvent.DigExpectedValue;
    }
    public override void Serialize(PacketSerializer archive) {
        SerializePlayerId(archive, archive.IsWriting ? ResolvePlayer(m_fromPlayerIndex) : null);
        archive.Enum(ref m_digEvent);
        archive.Value(ref m_netDigRay);
        archive.Value(ref m_netDigRaycast);
        archive.Value(ref m_hasDigTarget);
        archive.Value(ref m_digTarget);
        archive.Value(ref m_digOldValue);
        archive.Value(ref m_digExpectedValue);
    }
    public override void Handle(bool isServer) {
        if (!TryResolvePlayer(isServer, out PlayerData playerData)) return;
        playerData.ComponentPlayer.AddDigEvent(
            m_digEvent,
            m_netDigRay,
            m_netDigRaycast,
            m_hasDigTarget,
            m_digTarget,
            m_digOldValue,
            m_digExpectedValue
        );
        QueuePlayerPacket(this, isServer);
    }
}


