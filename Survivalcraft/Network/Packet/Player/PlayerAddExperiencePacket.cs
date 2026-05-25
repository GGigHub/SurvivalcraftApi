using Engine;
using TemplatesDatabase;

namespace Game.Network;

public class PlayerAddExperiencePacket : PlayerPacketBase {
    public override byte ID => (byte)PacketType.PlayerAddExperience;
    public int m_count;
    public bool m_playSound;
    public float m_level;
    public PlayerAddExperiencePacket() { }
    public PlayerAddExperiencePacket(PlayerData playerData, int count, bool playSound, float level) {
        m_fromPlayerId = NetworkManager.GetClientByGUID(playerData.NetworkGuid).ID;
        m_count = count;
        m_playSound = playSound;
        m_level = level;
    }
    public override void Serialize(PacketSerializer archive) {
        SerializePlayerId(archive, archive.IsWriting ? ResolvePlayer(m_fromPlayerId) : null);
        archive.Value(ref m_count);
        archive.Value(ref m_playSound);
        archive.Value(ref m_level);
    }
    public override void Handle(bool isServer) {
        if (isServer || !TryResolvePlayer(false, out PlayerData playerData)) return;
        ComponentPlayer player = playerData.ComponentPlayer;
        player.ComponentLevel.NetAddExperience(m_count, m_playSound);
        player.PlayerData.Level = m_level;
    }
}

