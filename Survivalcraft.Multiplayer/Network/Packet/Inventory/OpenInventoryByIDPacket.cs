namespace Game.Network;

public class OpenInventoryByIDPacket : Packet {
    public override byte ID => (byte)PacketType.OpenInventoryByID;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;

    public int m_inventoryId;
    public long m_requestId;

    public OpenInventoryByIDPacket() {
    }

    public OpenInventoryByIDPacket(IInventory inventory)
        : this(inventory?.Id ?? 0) {
    }

    public OpenInventoryByIDPacket(int inventoryId) {
        m_inventoryId = inventoryId;
        m_requestId = NetworkMutationRequestTracker.CreateRequestId();
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_inventoryId);
        archive.Value(ref m_requestId);
    }

    public override void Handle(bool isServer) {
        if (!isServer || From == null) {
            return;
        }
        NetworkContainerOpenResultCode result;
        IInventory inventory = null;
        if (m_requestId <= 0 || m_inventoryId <= 0) {
            result = NetworkContainerOpenResultCode.InvalidRequest;
        }
        else {
            inventory = GameManager.Project.FindSubsystem<SubsystemInventories>()
                .GetInventoryById(m_inventoryId);
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
            NetworkContainerOpenRequestKind.ById,
            result,
            inventory?.Id ?? m_inventoryId);
    }
}
