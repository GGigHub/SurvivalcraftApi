using Engine;

namespace Game.Network;

public class OpenInventoryByPointPacket : Packet {
    public override byte ID => (byte)PacketType.OpenInventoryByPoint;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;

    public Point3 m_point3;

    public OpenInventoryByPointPacket() {
    }

    public OpenInventoryByPointPacket(Point3 point3) {
        m_point3 = point3;
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_point3);
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            var subsystemBlockEntities = GameManager.Project.FindSubsystem<SubsystemBlockEntities>();
            ComponentBlockEntity blockEntity = subsystemBlockEntities.GetBlockEntity(m_point3.X, m_point3.Y, m_point3.Z);
            if (blockEntity != null) {
                IInventory inventory = blockEntity.Entity.FindComponent<IInventory>(throwOnError: false);
                if (inventory != null) {
                    OpenInventoryByIDPacket packet = new OpenInventoryByIDPacket(inventory);
                    packet.ToPlayerIndex = From?.PlayerIndex ?? -1;
                    NetworkService.QueuePacket(packet);
                }
            }
        }
    }
}

