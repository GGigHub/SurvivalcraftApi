using Engine;
using TemplatesDatabase;

namespace Game.Network;

public class PlayerStatSyncPacket : PlayerPacketBase {
    public override byte ID => (byte)PacketType.PlayerStatSync;
    public ValuesDictionary m_stat;
    public PlayerStatSyncPacket() { }
    public PlayerStatSyncPacket(ComponentPlayer player) {
        m_fromPlayerIndex = player.PlayerData.PlayerIndex;
        m_stat = new ValuesDictionary();
        player.PlayerStats.Save(m_stat);
    }
    public override void Serialize(PacketSerializer archive) {
        SerializePlayerId(archive, archive.IsWriting ? ResolvePlayer(m_fromPlayerIndex) : null);
        byte[] messagePack = archive.IsWriting ? m_stat.ToMessagePack() : null;
        archive.Buffer(ref messagePack);
        if (archive.IsReading) {
            m_stat = new ValuesDictionary();
            m_stat.ApplyOverridesUseMessagePack(messagePack);
        }
    }
    public override void Handle(bool isServer) {
        if (isServer || !TryResolvePlayer(false, out PlayerData playerData)) return;
        playerData.ComponentPlayer.PlayerStats.Load(m_stat);
    }
}


