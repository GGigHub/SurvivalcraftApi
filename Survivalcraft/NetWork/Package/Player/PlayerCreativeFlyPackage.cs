using Engine;
using TemplatesDatabase;

namespace Game.NetWork;

public class PlayerCreativeFlyPackage : PlayerPackageBase {
    public override byte ID => (byte)PackageType.PlayerCreativeFly;
    public bool m_isCreativeFly;
    public PlayerCreativeFlyPackage() { }
    public PlayerCreativeFlyPackage(ComponentPlayer player) {
        m_fromPlayerId = player.PlayerData?.ClientId ?? 0;
        m_isCreativeFly = player.ComponentLocomotion.IsCreativeFlyEnabled;
    }
    public override void Serialize(PackageSerializer archive) {
        SerializePlayerId(archive, archive.IsWriting ? PlayerPackageHelpers.ResolvePlayer(m_fromPlayerId) : null);
        archive.Value(ref m_isCreativeFly);
    }
    public override void Handle(bool isServer) {
        if (!TryResolvePlayer(isServer, out PlayerData playerData)) return;
        if (isServer && NetworkManager.GetEffectiveGameMode(From.PlayerData) != GameMode.Creative) {
            NetworkManager.Server.RemoveClient(From, "鐞涘奔璐熷鍌氱埗");
            return;
        }
        playerData.ComponentPlayer.ComponentLocomotion.IsCreativeFlyEnabled = m_isCreativeFly;
        PlayerPackageHelpers.QueuePlayerPackage(this, isServer);
    }
}