using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game.Network;

public class PlayerModifyPacket : Packet {
    public const int MaximumPlayerNameUtf8Bytes = 256;
    public override byte ID => (byte)PacketType.PlayerModify;
    public override NetworkState MinNeedState => NetworkState.Connected;
    public override bool RequiresProject => true;
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
        archive.Value(ref m_playerName, MaximumPlayerNameUtf8Bytes);
        archive.Value(ref m_skinName, SkinPacketBase.MaximumSkinNameUtf8Bytes);
        archive.Enum(ref m_playerClass);
    }

    public override void Handle(bool isServer) {
        if (m_playerIndex < 0
            || string.IsNullOrWhiteSpace(m_playerName)
            || m_playerName.Any(char.IsControl)
            || !Enum.IsDefined(m_playerClass)
            || !IsValidSkinName(m_skinName)
            || isServer && (From == null || From.PlayerIndex != m_playerIndex)) {
            return;
        }
        var subsystemPlayers = GameManager.Project.FindSubsystem<SubsystemPlayers>();
        var playerData = subsystemPlayers.FindPlayerData(p => p.PlayerIndex == m_playerIndex);
        if (playerData != null) {
            playerData.Name = m_playerName;
            playerData.CharacterSkinName = m_skinName;
            playerData.m_playerClass = m_playerClass;
            if (isServer) {
                NetworkManager.Queue(this);
            }
        }
    }

    private static bool IsValidSkinName(string skinName) {
        if (string.IsNullOrWhiteSpace(skinName)
            || skinName.Any(char.IsControl)) {
            return false;
        }
        if (CharacterSkinsManager.IsBuiltIn(skinName)) {
            return skinName.Length <= 64
                && skinName.Skip(1).All(character =>
                    char.IsAsciiLetterOrDigit(character)
                    || character is ' ' or '-' or '_');
        }
        return SkinPacketBase.TryValidateSkinName(skinName, out _);
    }
}

