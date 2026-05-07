using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game.NetWork;

public class PlayerNoMsgAddPackage : IPackage {
    public override byte ID => (byte)PackageType.PlayerNoMsgAdd;
    public override ClientStage MinNeedState => ClientStage.Connected;
    public Guid m_playerGuid;

    public PlayerNoMsgAddPackage() { }

    public PlayerNoMsgAddPackage(Guid guid) {
        m_playerGuid = guid;
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_playerGuid);
    }

    public override void Handle(bool isServer) {
        if (isServer) return;
        GameManager.Project.FindSubsystem<SubsystemPlayers>().NoMsgPlayerGuidList.Add(m_playerGuid.ToString());
    }
}