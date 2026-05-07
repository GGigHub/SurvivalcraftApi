using Engine;
using TemplatesDatabase;

namespace Game.NetWork;

public class PlayerDigPackage : PlayerPackageBase {
    public override byte ID => (byte)PackageType.PlayerDig;
    public ComponentPlayer.DigEvent m_digEvent;
    public Ray3? m_netDigRay;
    public TerrainRaycastResult? m_netDigRaycast;
    public PlayerDigPackage() { }
    public PlayerDigPackage(ComponentPlayer player) {
        m_fromPlayerId = player.PlayerData?.ClientId ?? 0;
        m_digEvent = player.digEvent.digEvent;
        m_netDigRay = player.digEvent.NetDigRay;
        m_netDigRaycast = player.digEvent.NetDigRaycast;
    }
    public override void Serialize(PackageSerializer archive) {
        SerializePlayerId(archive, archive.IsWriting ? PlayerPackageHelpers.ResolvePlayer(m_fromPlayerId) : null);
        archive.Enum(ref m_digEvent);
        archive.Value(ref m_netDigRay);
        archive.Value(ref m_netDigRaycast);
    }
    public override void Handle(bool isServer) {
        if (!TryResolvePlayer(isServer, out PlayerData playerData)) return;
        playerData.ComponentPlayer.AddDigEvent(m_digEvent, m_netDigRay, m_netDigRaycast);
        PlayerPackageHelpers.QueuePlayerPackage(this, isServer);
    }
}