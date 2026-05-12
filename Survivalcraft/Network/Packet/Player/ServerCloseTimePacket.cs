using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game.Network;

public class ServerCloseTimePacket : Packet {
    public override byte ID => (byte)PacketType.ServerCloseTime;
    public override ClientState MinNeedState => ClientState.Connected;
    public int m_time;
    public string m_message;

    public ServerCloseTimePacket() { }

    public ServerCloseTimePacket(int time, string message) {
        m_time = time;
        m_message = message;
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_time);
        archive.Value(ref m_message);
    }

    public override void Handle(bool isServer) {
        if (isServer) return;
        var p3 = GameManager.Project.FindSubsystem<SubsystemPlayers>().MainPlayer;
        if (p3 != null) {
            p3.ComponentGui.CloseTime = m_time;
            DialogsManager.ShowDialog(null, new MessageDialog("服务器关闭提示", m_message, LanguageControl.Yes, LanguageControl.No, btn => {
                DialogsManager.HideAllDialogs();
            }));
        }
    }
}

