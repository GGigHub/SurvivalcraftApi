using Engine;
using TemplatesDatabase;

namespace Game.Network;

public class PlayerCreativeFlyPacket : PlayerPacketBase {
    public override byte ID => (byte)PacketType.PlayerCreativeFly;
    public bool m_isCreativeFly;
    public PlayerCreativeFlyPacket() { }
    public PlayerCreativeFlyPacket(ComponentPlayer player) {
        m_fromPlayerId = player.PlayerData?.ClientId ?? 0;
        m_isCreativeFly = player.ComponentLocomotion.IsCreativeFlyEnabled;
    }
    public override void Serialize(PacketSerializer archive) {
        SerializePlayerId(archive, archive.IsWriting ? ResolvePlayer(m_fromPlayerId) : null);
        archive.Value(ref m_isCreativeFly);
    }
    public override void Handle(bool isServer) {
        if (!TryResolvePlayer(isServer, out PlayerData playerData)) return;
        if (isServer && NetworkManager.GetEffectiveGameMode(From.PlayerData) != GameMode.Creative) {
            NetworkManager.Server.RemoveClient(From, "非法操作");
            return;
        }
        playerData.ComponentPlayer.ComponentLocomotion.IsCreativeFlyEnabled = m_isCreativeFly;
        QueuePlayerPacket(this, isServer);
    }
}

