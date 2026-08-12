using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game.Network;

public class PlayerCreatePacket : Packet {
    public override byte ID => (byte)PacketType.PlayerCreate;
    public override NetworkState MinNeedState => NetworkState.Connected;
    public ValuesDictionary m_vd;

    public PlayerCreatePacket() { }

    public PlayerCreatePacket(PlayerData playerData) {
        m_vd = new ValuesDictionary();
        playerData.Save(m_vd);
    }

    public override void Serialize(PacketSerializer archive) {
        byte[] messagePack = archive.IsWriting ? m_vd.ToMessagePack() : null;
        archive.Buffer(ref messagePack);
        if (archive.IsReading) {
            m_vd = new ValuesDictionary();
            m_vd.ApplyOverridesUseMessagePack(messagePack);
        }
    }

    public override void Handle(bool isServer) {
        if (!isServer) return;
        var subsystemPlayers = GameManager.Project.FindSubsystem<SubsystemPlayers>();
        var playerData = new PlayerData(GameManager.Project);
        playerData.Load(m_vd);
        subsystemPlayers.AddPlayerData(playerData);
        // 服务端广播给所有客户端，添加玩家
        NetworkManager.Queue(new PlayerAddPacket(playerData));

        From.PlayerIndex = playerData.PlayerIndex;
        NetworkManager.Queue(new IndexPacket(From.PlayerIndex) { To = From });

        // 广播 "加入游戏" 系统消息，并在主机本地回显。
        string joinName = string.IsNullOrEmpty(playerData.Name) ? $"Player {playerData.PlayerIndex}" : playerData.Name;
        NetworkManager.Queue(new ChatMessagePacket(ChatMessageType.Join, playerData.PlayerIndex, joinName, string.Empty));
        if (!NetworkManager.IsClientRunning) {
            Survivalcraft.Multiplayer.ChatManager.Add(ChatMessageType.Join, playerData.PlayerIndex, joinName, string.Empty);
        }
    }
}
