using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game.Network;

public class PlayerCreatePacket : Packet {
    public override byte ID => (byte)PacketType.PlayerCreate;
    public override ClientStage MinNeedState => ClientStage.Connected;
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
        // 服务端广播给所有客户端，添加玩家�?
        foreach (var client in NetworkManager.Clients) {
            if (client.Value == NetworkManager.Self) continue;
            NetworkManager.QueuePacket(new PlayerAddPacket(playerData));
        }
    }
}

