using Engine;
using TemplatesDatabase;

namespace Game.Network;

public class PlayerCreativeFlyPacket : PlayerPacketBase {
    public override byte ID => (byte)PacketType.PlayerCreativeFly;
    public bool m_isCreativeFly;
    public PlayerCreativeFlyPacket() { }
    public PlayerCreativeFlyPacket(ComponentPlayer player) {
        m_fromPlayerIndex = player.PlayerData.PlayerIndex;
        m_isCreativeFly = player.ComponentLocomotion.IsCreativeFlyEnabled;
    }
    public override void Serialize(PacketSerializer archive) {
        SerializePlayerId(archive, archive.IsWriting ? ResolvePlayer(m_fromPlayerIndex) : null);
        archive.Value(ref m_isCreativeFly);
    }
    public override void Handle(bool isServer) {
        if (!TryResolvePlayer(isServer, out PlayerData playerData)) return;
        if (isServer && GameManager.Project.FindSubsystem<SubsystemGameInfo>()?.WorldSettings?.GameMode != GameMode.Creative) {
            NetworkService.Server.RemoveClient(From, "非法操作");
            return;
        }
        playerData.ComponentPlayer.ComponentLocomotion.IsCreativeFlyEnabled = m_isCreativeFly;
        QueuePlayerPacket(this, isServer);
    }
}


