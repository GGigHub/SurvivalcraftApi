using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game.NetWork;

public class PlayerNoMsgRemovePackage : IPackage {
    public override byte ID => (byte)PackageType.PlayerNoMsgRemove;
    public override ClientStage MinNeedState => ClientStage.Connected;
    public Guid m_playerGuid;

    public PlayerNoMsgRemovePackage() { }

    public PlayerNoMsgRemovePackage(Guid guid) {
        m_playerGuid = guid;
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_playerGuid);
    }

    public override void Handle(bool isServer) {
        if (isServer) return;
        GameManager.Project.FindSubsystem<SubsystemPlayers>().NoMsgPlayerGuidList.Remove(m_playerGuid.ToString());
    }
}