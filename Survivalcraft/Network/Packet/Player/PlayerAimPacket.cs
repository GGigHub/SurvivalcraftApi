using Engine;
using TemplatesDatabase;

namespace Game.Network;

public class PlayerAimPacket : PlayerPacketBase {
    public override byte ID => (byte)PacketType.PlayerAim;
    public ComponentPlayer.AimEvent m_aimEvent;
    public Ray3? m_netAimRay;
    public PlayerAimPacket() { }
    public PlayerAimPacket(ComponentPlayer player) {
        m_fromPlayerId = NetworkService.GetClientByGUID(player.PlayerData.NetworkGuid).ID;
        m_aimEvent = player.aimEvent.aimEvent;
        m_netAimRay = player.aimEvent.NetAim;
    }
    public override void Serialize(PacketSerializer archive) {
        SerializePlayerId(archive, archive.IsWriting ? ResolvePlayer(m_fromPlayerId) : null);
        archive.Enum(ref m_aimEvent);
        archive.Value(ref m_netAimRay);
    }
    public override void Handle(bool isServer) {
        if (!TryResolvePlayer(isServer, out PlayerData playerData)) return;
        playerData.ComponentPlayer.AddAimEvent(m_aimEvent, m_netAimRay);
        QueuePlayerPacket(this, isServer);
    }
}

