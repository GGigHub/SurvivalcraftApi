using Engine;
using TemplatesDatabase;

namespace Game.NetWork;

public class PlayerAimPackage : PlayerPackageBase {
    public override byte ID => (byte)PackageType.PlayerAim;
    public ComponentPlayer.AimEvent m_aimEvent;
    public Ray3? m_netAimRay;
    public PlayerAimPackage() { }
    public PlayerAimPackage(ComponentPlayer player) {
        m_fromPlayerId = player.PlayerData?.ClientId ?? 0;
        m_aimEvent = player.aimEvent.aimEvent;
        m_netAimRay = player.aimEvent.NetAim;
    }
    public override void Serialize(PackageSerializer archive) {
        SerializePlayerId(archive, archive.IsWriting ? PlayerPackageHelpers.ResolvePlayer(m_fromPlayerId) : null);
        archive.Enum(ref m_aimEvent);
        archive.Value(ref m_netAimRay);
    }
    public override void Handle(bool isServer) {
        if (!TryResolvePlayer(isServer, out PlayerData playerData)) return;
        playerData.ComponentPlayer.AddAimEvent(m_aimEvent, m_netAimRay);
        PlayerPackageHelpers.QueuePlayerPackage(this, isServer);
    }
}