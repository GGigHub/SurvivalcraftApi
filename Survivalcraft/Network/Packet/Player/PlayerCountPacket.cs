using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game.Network;

public class PlayerCountPacket : Packet {
    public override byte ID => (byte)PacketType.PlayerCount;
    public override ClientState MinNeedState => ClientState.Connected;
    public int m_playerCount;

    public PlayerCountPacket() { }

    public PlayerCountPacket(int count) {
        m_playerCount = count;
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_playerCount);
    }

    public override void Handle(bool isServer) {
        if (isServer) return;
        ComponentPlayer mainPlayer = GameManager.Project.FindSubsystem<SubsystemPlayers>().MainPlayer;
        if (mainPlayer != null) {
            int clientPlayerCount = GameManager.Project.FindSubsystem<SubsystemPlayers>().PlayersData.Count;
            Log.Information($"隐身测试，服务端人数：{m_playerCount}; 客户端人数：{clientPlayerCount}");
            if (clientPlayerCount != m_playerCount) {
                ScreensManager.SwitchScreen("NetPlay");
                GameManager.DisposeProject();
                NetworkManager.Stop();
                DialogsManager.ShowDialog(null, new MessageDialog("连接异常", "检测到玩家人数异常，请重新连接服务器", LanguageControl.Ok, null, btn => {
                }));
            }
        }
    }
}

