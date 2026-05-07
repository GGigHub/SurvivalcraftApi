using Engine;
using TemplatesDatabase;

namespace Game.Network;

public class PlayerPositionSetPacket : PlayerPacketBase {
    public override byte ID => (byte)PacketType.PlayerPositionSet;
    public Vector3 Position;
    public Vector3 Velocity;
    public PlayerPositionSetPacket() { }
    public PlayerPositionSetPacket(ComponentPlayer player) {
        m_fromPlayerId = player.PlayerData?.ClientId ?? 0;
        Position = player.ComponentBody.Position;
        Velocity = player.ComponentBody.Velocity;
    }
    public override void Serialize(PacketSerializer archive) {
        SerializePlayerId(archive, archive.IsWriting ? ResolvePlayer(m_fromPlayerId) : null);
        archive.Value(ref Position);
        archive.Value(ref Velocity);
    }
    public override void Handle(bool isServer) {
        if (isServer || !TryResolvePlayer(false, out PlayerData playerData)) return;
        playerData.ComponentPlayer.ComponentBody.Position = Position;
        playerData.ComponentPlayer.ComponentBody.Velocity = Velocity;
    }
}

