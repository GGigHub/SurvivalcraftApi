using Engine;

namespace Game.Network;

public class EditSignPacket : Packet {
    public override byte ID => (byte)PacketType.EditSign;
    public override ClientState MinNeedState => ClientState.ProjectLoaded;

    public Point3 m_point3;

    public EditSignPacket() {
    }

    public EditSignPacket(Point3 point) {
        m_point3 = point;
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_point3);
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            To = From;
            NetworkManager.QueuePacket(this);
        }
        else {
            DialogsManager.ShowDialog(NetworkManager.MainPlayer.GuiWidget, new EditSignDialog(GameManager.Project.FindSubsystem<SubsystemSignBlockBehavior>(), m_point3));
        }
    }
}

