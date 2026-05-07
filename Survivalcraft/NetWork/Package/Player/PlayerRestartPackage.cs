using Engine;
using TemplatesDatabase;

namespace Game.NetWork;

public class PlayerRestartPackage : PlayerPackageBase {
    public override byte ID => (byte)PackageType.PlayerRestart;
    public PlayerRestartPackage() { }
    public PlayerRestartPackage(PlayerData playerData) {
        m_fromPlayerId = playerData?.ClientId ?? 0;
    }
    public override void Serialize(PackageSerializer archive) {
        SerializePlayerId(archive, archive.IsWriting ? PlayerPackageHelpers.ResolvePlayer(m_fromPlayerId) : null);
    }
    public override void Handle(bool isServer) {
        if (!TryResolvePlayer(isServer, out PlayerData playerData)) return;
        playerData.readyToRestart = true;
    }
}