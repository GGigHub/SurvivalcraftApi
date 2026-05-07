using Engine;

namespace Game.NetWork;

public class OpenInventoryByPointPackage : IPackage {
    public override byte ID => (byte)PackageType.OpenInventoryByPoint;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;

    public Point3 m_point3;

    public OpenInventoryByPointPackage() {
    }

    public OpenInventoryByPointPackage(Point3 point3) {
        m_point3 = point3;
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_point3);
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            var subsystemBlockEntities = GameManager.Project.FindSubsystem<SubsystemBlockEntities>();
            ComponentBlockEntity blockEntity = subsystemBlockEntities.GetBlockEntity(m_point3.X, m_point3.Y, m_point3.Z);
            if (blockEntity != null) {
                IInventory inventory = blockEntity.Entity.FindComponent<IInventory>(throwOnError: false);
                if (inventory != null) {
                    OpenInventoryByIDPackage package = new OpenInventoryByIDPackage(inventory);
                    package.To = From;
                    NetworkManager.QueuePackage(package);
                }
            }
        }
    }
}