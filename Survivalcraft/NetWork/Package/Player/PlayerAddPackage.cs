using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game.NetWork;

public class PlayerAddPackage : IPackage {
    public override byte ID => (byte)PackageType.PlayerAdd;
    public override ClientStage MinNeedState => ClientStage.Connected;
    public ValuesDictionary m_vd;

    public PlayerAddPackage() { }

    public PlayerAddPackage(PlayerData playerData) {
        m_vd = new ValuesDictionary();
        playerData.Save(m_vd);
    }

    public override void Serialize(PackageSerializer archive) {
        byte[] messagePack = archive.IsWriting ? m_vd.ToMessagePack() : null;
        archive.Buffer(ref messagePack);
        if (archive.IsReading) {
            m_vd = new ValuesDictionary();
            m_vd.ApplyOverridesUseMessagePack(messagePack);
        }
    }

    public override void Handle(bool isServer) {
        if (isServer) return;
        var subsystemPlayers = GameManager.Project.FindSubsystem<SubsystemPlayers>();
        var playerData = new PlayerData(GameManager.Project);
        playerData.Load(m_vd);
        subsystemPlayers.AddPlayerData(playerData);
        NetworkManager.QueuePackage(new PlayerKnownPackage(playerData.PlayerGUID));
    }
}