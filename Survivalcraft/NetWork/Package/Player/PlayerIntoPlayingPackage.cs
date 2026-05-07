using Engine;
using TemplatesDatabase;

namespace Game.NetWork;

public class PlayerIntoPlayingPackage : PlayerPackageBase {
    public override byte ID => (byte)PackageType.PlayerIntoPlaying;
    public PlayerIntoPlayingPackage() { }
    public PlayerIntoPlayingPackage(ComponentPlayer player) {
        m_fromPlayerId = player.PlayerData?.ClientId ?? 0;
    }
    public override void Serialize(PackageSerializer archive) {
        SerializePlayerId(archive, archive.IsWriting ? PlayerPackageHelpers.ResolvePlayer(m_fromPlayerId) : null);
    }
    public override void Handle(bool isServer) {
        if (isServer || !TryResolvePlayer(false, out PlayerData playerData)) return;
        playerData.ComponentPlayer.ComponentHealth.IsInvulnerable = false;
    }
}