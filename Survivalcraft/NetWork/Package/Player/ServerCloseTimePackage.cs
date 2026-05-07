using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game.NetWork;

public class ServerCloseTimePackage : IPackage {
    public override byte ID => (byte)PackageType.ServerCloseTime;
    public override ClientStage MinNeedState => ClientStage.Connected;
    public int m_time;
    public string m_message;

    public ServerCloseTimePackage() { }

    public ServerCloseTimePackage(int time, string message) {
        m_time = time;
        m_message = message;
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_time);
        archive.Value(ref m_message);
    }

    public override void Handle(bool isServer) {
        if (isServer) return;
        var p3 = GameManager.Project.FindSubsystem<SubsystemPlayers>().MainPlayer;
        if (p3 != null) {
            p3.ComponentGui.CloseTime = m_time;
            DialogsManager.ShowDialog(null, new MessageDialog("服务器关闭提醒", m_message, LanguageControl.Yes, LanguageControl.No, btn => {
                DialogsManager.HideAllDialogs();
            }));
        }
    }
}