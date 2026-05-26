

namespace Game.Network;

public class FireBlockRemovePacket : Packet {
    public override byte ID => (byte)PacketType.FireBlockRemove;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;
    public int x;
    public int y;
    public int z;

    public FireBlockRemovePacket() { }
    public FireBlockRemovePacket(int x, int y, int z) {
        this.x = x;
        this.y = y;
        this.z = z;
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref x);
        archive.Value(ref y);
        archive.Value(ref z);
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        GameManager.Project.FindSubsystem<SubsystemFireBlockBehavior>().RemoveFire(x, y, z, true);
    }
}

