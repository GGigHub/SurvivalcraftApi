

namespace Game.NetWork;

public class FireBlockRemovePackage : IPackage {
    public override byte ID => (byte)PackageType.FireBlockRemove;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public int x;
    public int y;
    public int z;

    public FireBlockRemovePackage() { }
    public FireBlockRemovePackage(int x, int y, int z) {
        this.x = x;
        this.y = y;
        this.z = z;
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref x);
        archive.Value(ref y);
        archive.Value(ref z);
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        GameManager.Project.FindSubsystem<SubsystemFireBlockBehavior>().RemoveFireNet(x, y, z);
    }
}