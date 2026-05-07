using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game.NetWork;

public class PlayerCountPackage : IPackage {
    public override byte ID => (byte)PackageType.PlayerCount;
    public override ClientStage MinNeedState => ClientStage.Connected;
    public int m_playerCount;

    public PlayerCountPackage() { }

    public PlayerCountPackage(int count) {
        m_playerCount = count;
    }

    public override void Serialize(PackageSerializer archive) {
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