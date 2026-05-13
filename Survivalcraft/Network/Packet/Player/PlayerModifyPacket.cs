using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game.Network;

public class PlayerModifyPacket : Packet {
    public override byte ID => (byte)PacketType.PlayerModify;
    public override ClientState MinNeedState => ClientState.Connected;
    public Guid m_playerGuid;
    public string m_playerName;
    public string m_skinName;
    public PlayerClass m_playerClass;

    public PlayerModifyPacket() { }

    public PlayerModifyPacket(PlayerData playerData) {
        m_playerGuid = playerData.NetworkGuid;
        m_playerName = playerData.Name;
        m_skinName = playerData.CharacterSkinName;
        m_playerClass = playerData.m_playerClass;
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_playerGuid);
        archive.Value(ref m_playerName);
        archive.Value(ref m_skinName);
        archive.Enum(ref m_playerClass);
    }

    public override void Handle(bool isServer) {
        var subsystemPlayers = GameManager.Project.FindSubsystem<SubsystemPlayers>();
        var playerData = subsystemPlayers.FindPlayerData(p => p.NetworkGuid == m_playerGuid);
        if (playerData != null) {
            playerData.CharacterSkinName = m_skinName;
            playerData.m_playerClass = m_playerClass;
            if (isServer) {
                NetworkManager.QueuePacket(this);
            }
        }
    }
}

