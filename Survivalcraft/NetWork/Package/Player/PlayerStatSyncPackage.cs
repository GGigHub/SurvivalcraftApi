using Engine;
using TemplatesDatabase;

namespace Game.NetWork;

public class PlayerStatSyncPackage : PlayerPackageBase {
    public override byte ID => (byte)PackageType.PlayerStatSync;
    public ValuesDictionary m_stat;
    public PlayerStatSyncPackage() { }
    public PlayerStatSyncPackage(ComponentPlayer player) {
        m_fromPlayerId = player.PlayerData?.ClientId ?? 0;
        m_stat = new ValuesDictionary();
        player.PlayerStats.Save(m_stat);
    }
    public override void Serialize(PackageSerializer archive) {
        SerializePlayerId(archive, archive.IsWriting ? PlayerPackageHelpers.ResolvePlayer(m_fromPlayerId) : null);
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