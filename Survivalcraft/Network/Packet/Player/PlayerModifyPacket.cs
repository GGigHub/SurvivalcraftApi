using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game.Network;

public class PlayerModifyPacket : Packet {
    public override byte ID => (byte)PacketType.PlayerModify;
    public override NetworkState MinNeedState => NetworkState.Connected;
    public int m_playerIndex;
    public string m_playerName;
    public string m_skinName;
    public PlayerClass m_playerClass;

    public PlayerModifyPacket() { }

    public PlayerModifyPacket(PlayerData playerData) {
        m_playerIndex = playerData.PlayerIndex;
        m_playerName = playerData.Name;
        m_skinName = playerData.CharacterSkinName;
        m_playerClass = playerData.m_playerClass;
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_playerIndex);
        archive.Value(ref m_playerName);
        archive.Value(ref m_skinName);
        archive.Enum(ref m_playerClass);
    }

    public override void Handle(bool isServer) {
        var subsystemPlayers = GameManager.Project.FindSubsystem<SubsystemPlayers>();
        var playerData = subsystemPlayers.FindPlayerData(p => p.PlayerIndex == m_playerIndex);
        if (playerData != null) {
            playerData.CharacterSkinName = m_skinName;
            playerData.m_playerClass = m_playerClass;
            if (isServer) {
                NetworkManager.Queue(this);
            }
        }
    }
}

