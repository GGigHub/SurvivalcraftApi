using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game.NetWork;

public class PlayerModifyPackage : IPackage {
    public override byte ID => (byte)PackageType.PlayerModify;
    public override ClientStage MinNeedState => ClientStage.Connected;
    public Guid m_playerGuid;
    public string m_playerName;
    public string m_skinName;
    public PlayerClass m_playerClass;

    public PlayerModifyPackage() { }

    public PlayerModifyPackage(PlayerData playerData) {
        m_playerGuid = playerData.PlayerGUID;
        m_playerName = playerData.Name;
        m_skinName = playerData.CharacterSkinName;
        m_playerClass = playerData.m_playerClass;
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_playerGuid);
        archive.Value(ref m_playerName);
        archive.Value(ref m_skinName);
        archive.Enum(ref m_playerClass);
    }

    public override void Handle(bool isServer) {
        var subsystemPlayers = GameManager.Project.FindSubsystem<SubsystemPlayers>();
        var playerData = subsystemPlayers.FindPlayerData(p => p.PlayerGUID == m_playerGuid);
        if (playerData != null) {
            playerData.CharacterSkinName = m_skinName;
            playerData.m_playerClass = m_playerClass;
            if (isServer) {
                NetworkManager.QueuePackage(this);
            }
        }
    }
}