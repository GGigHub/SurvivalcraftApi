using Engine;

namespace Game.Network;

public class OpenInventoryByPointPacket : Packet {
    public override byte ID => (byte)PacketType.OpenInventoryByPoint;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;

    public Point3 m_point3;
    public long m_requestId;

    public OpenInventoryByPointPacket() {
    }

    public OpenInventoryByPointPacket(Point3 point3) {
        m_point3 = point3;
        m_requestId = NetworkMutationRequestTracker.CreateRequestId();
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_point3);
        archive.Value(ref m_requestId);
    }

    public override void Handle(bool isServer) {
        if (!isServer || From == null) {
            return;
        }
        NetworkContainerOpenResultCode result;
        IInventory inventory = null;
        if (m_requestId <= 0 || m_point3.Y is < 0 or > 255) {
            result = NetworkContainerOpenResultCode.InvalidRequest;
        }
        else {
            ComponentBlockEntity blockEntity = GameManager.Project
                .FindSubsystem<SubsystemBlockEntities>()
                .GetBlockEntity(m_point3.X, m_point3.Y, m_point3.Z);
            inventory = blockEntity?.Entity.FindComponent<IInventory>(throwOnError: false);
            result = inventory == null
                ? NetworkContainerOpenResultCode.InventoryNotFound
                : !InventoryPacketHelpers.CanSessionAccessInventory(inventory, From)
                    ? NetworkContainerOpenResultCode.PermissionDenied
                    : !OpenInventoryResultPacket.IsSupportedInventory(inventory)
                        ? NetworkContainerOpenResultCode.UnsupportedInventory
                        : NetworkContainerOpenResultCode.Accepted;
        }
        OpenInventoryResultPacket.Queue(
            From,
            m_requestId,
            NetworkContainerOpenRequestKind.ByPoint,
            result,
            inventory?.Id ?? 0);
    }
}
