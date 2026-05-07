using Engine;
using TemplatesDatabase;

namespace Game.NetWork;

public abstract class PlayerPackageBase : IPackage {
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public byte m_fromPlayerId;

    protected void SerializePlayerId(PackageSerializer archive, PlayerData playerData) {
        PlayerPackageHelpers.SerializeFromPlayerId(archive, ref m_fromPlayerId, playerData);
    }

    protected bool TryResolvePlayer(bool isServer, out PlayerData playerData) {
        return PlayerPackageHelpers.TryResolvePlayer(m_fromPlayerId, isServer, From, out playerData);
    }
}