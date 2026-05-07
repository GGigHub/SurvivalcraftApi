using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game.NetWork;

public class PlayerKnownPackage : IPackage {
    public override byte ID => (byte)PackageType.PlayerKnown;
    public override ClientStage MinNeedState => ClientStage.Connected;
    public Guid m_playerGuid;

    public PlayerKnownPackage() { }

    public PlayerKnownPackage(Guid guid) {
        m_playerGuid = guid;
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_playerGuid);
    }

    public override void Handle(bool isServer) {
        // Typically server side would handle this to know client has loaded the player
    }
}