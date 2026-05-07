using Engine;
using TemplatesDatabase;

namespace Game.NetWork;

public class PlayerInteractPackage : PlayerPackageBase {
    public override byte ID => (byte)PackageType.PlayerInteract;
    public ComponentPlayer.InteractEvent m_interactEvent;
    public Ray3 m_netInteractRay;
    public TerrainRaycastResult? m_netInteractRaycast;

    public PlayerInteractPackage() { }
    public PlayerInteractPackage(ComponentPlayer player) {
        m_fromPlayerId = player.PlayerData?.ClientId ?? 0;
        m_interactEvent = player.interactEvent.interactEvent;
        m_netInteractRay = player.interactEvent.NetInteractRay;
        m_netInteractRaycast = player.interactEvent.NetPlaceRaycast;
    }
    public override void Serialize(PackageSerializer archive) {
        SerializePlayerId(archive, archive.IsWriting ? PlayerPackageHelpers.ResolvePlayer(m_fromPlayerId) : null);
        archive.Enum(ref m_interactEvent);
        archive.Value(ref m_netInteractRay);
        archive.Value(ref m_netInteractRaycast);
    }
    public override void Handle(bool isServer) {
        if (!TryResolvePlayer(isServer, out PlayerData playerData)) return;
        playerData.ComponentPlayer.AddInteractEvent(m_interactEvent, m_netInteractRay, m_netInteractRaycast);
        PlayerPackageHelpers.QueuePlayerPackage(this, isServer);
    }
}