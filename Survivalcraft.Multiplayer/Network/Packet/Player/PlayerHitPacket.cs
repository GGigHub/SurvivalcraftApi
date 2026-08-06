using Engine;
using TemplatesDatabase;

namespace Game.Network;

public class PlayerHitPacket : PlayerPacketBase {
    public override byte ID => (byte)PacketType.PlayerHit;
    public int bodyId;
    public Vector3 m_hitPosition;
    public Vector3 m_hitDirection;
    public PlayerHitPacket() { }
    public PlayerHitPacket(ComponentPlayer player, ComponentBody body, Vector3 hitPosition, Vector3 hitDirection) {
        m_fromPlayerIndex = player.PlayerData.PlayerIndex;
        bodyId = body.Entity.Id;
        m_hitPosition = hitPosition;
        m_hitDirection = hitDirection;
    }
    public override void Serialize(PacketSerializer archive) {
        SerializePlayerId(archive, archive.IsWriting ? ResolvePlayer(m_fromPlayerIndex) : null);
        archive.Value(ref bodyId);
        archive.Value(ref m_hitPosition);
        archive.Value(ref m_hitDirection);
    }
    public override void Handle(bool isServer) {
        if (!TryResolvePlayer(isServer, out PlayerData playerData)) return;
        playerData.ComponentPlayer.AddHitEvent(bodyId, m_hitPosition, m_hitDirection);
        QueuePlayerPacket(this, isServer);
    }
}


