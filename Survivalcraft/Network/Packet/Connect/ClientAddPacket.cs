using Engine;

namespace Game.Network;

public class ClientAddPacket : ClientPacketBase{
    public override byte ID => (byte)PacketType.ClientAdd;
    public override NetworkState MinNeedState => NetworkState.NotConnected;
    public int m_playerIndex;
    public Guid m_networkGuid;
    public string m_nickname;

    public ClientAddPacket() { }

    public ClientAddPacket(int playerIndex, Guid guid) {
        m_playerIndex = playerIndex;
        m_networkGuid = guid;
    }

    public override void Serialize(PacketSerializer archive) {
        SerializePlayerInfo(archive, ref m_playerIndex, ref m_networkGuid);
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        // 临时适配代码，等 NetworkService 彻底重构后将移除
        NetworkService.AddClient(new ClientSession { PlayerIndex = m_playerIndex, NetworkGuid = m_networkGuid });
    }
}

