using Engine;
using TemplatesDatabase;

namespace Game.NetWork;

public class PlayerAddExperiencePackage : PlayerPackageBase {
    public override byte ID => (byte)PackageType.PlayerAddExperience;
    public int m_count;
    public bool m_playSound;
    public float m_level;
    public PlayerAddExperiencePackage() { }
    public PlayerAddExperiencePackage(PlayerData playerData, int count, bool playSound, float level) {
        m_fromPlayerId = playerData?.ClientId ?? 0;
        m_count = count;
        m_playSound = playSound;
        m_level = level;
    }
    public override void Serialize(PackageSerializer archive) {
        SerializePlayerId(archive, archive.IsWriting ? PlayerPackageHelpers.ResolvePlayer(m_fromPlayerId) : null);
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