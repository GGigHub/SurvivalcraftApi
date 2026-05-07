

namespace Game.Network;

public class FireBlockAddPacket : Packet {
    public override byte ID => (byte)PacketType.FireBlockAdd;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public int x;
    public int y;
    public int z;
    public float m_duration;

    public FireBlockAddPacket() { }
    public FireBlockAddPacket(int x, int y, int z, float expandability) {
        this.x = x;
        this.y = y;
        this.z = z;
        m_duration = expandability;
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref x);
        archive.Value(ref y);
        archive.Value(ref z);
        archive.Value(ref m_duration);
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        GameManager.Project.FindSubsystem<SubsystemFireBlockBehavior>().AddFireNet(x, y, z, m_duration);
    }
}

