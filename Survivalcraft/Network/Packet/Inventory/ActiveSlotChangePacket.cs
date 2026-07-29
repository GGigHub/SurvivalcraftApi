using Engine;
using GameEntitySystem;

namespace Game.Network;

public class ActiveSlotChangePacket : Packet {
    public override byte ID => (byte)PacketType.ActiveSlotChange;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;
    public int m_inventoryId;
    public int m_activeSlotIndex;
    public long m_revision = -1;
    public long m_requestId;

    public ActiveSlotChangePacket() {
    }

    public ActiveSlotChangePacket(IInventory inventory, long revision = -1) {
        m_inventoryId = inventory.Id;
        m_activeSlotIndex = inventory.ActiveSlotIndex;
        m_revision = revision;
        m_requestId = NetworkMutationRequestTracker.CreateRequestId();
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_inventoryId);
        archive.Value(ref m_activeSlotIndex);
        archive.Value(ref m_revision);
        archive.Value(ref m_requestId);
    }

    public override void Handle(bool isServer) {
        var subsystemInventories = GameManager.Project.FindSubsystem<SubsystemInventories>();
        IInventory inventory = subsystemInventories.GetInventoryById(m_inventoryId);
        if (inventory == null
            || !InventoryPacketHelpers.IsActiveSlotIndexValid(inventory, m_activeSlotIndex)) {
            return;
        }
        if (isServer) {
            if (!InventoryPacketHelpers.IsOwnedBySession(inventory, From)) {
                InventoryPacketHelpers.QueueInventorySync(inventory, From);
                return;
            }
            if (!NetworkMutationRequestTracker.TryAccept(
                    From,
                    NetworkMutationDomain.InventorySelectSlot,
                    m_requestId)) {
                InventoryPacketHelpers.QueueInventorySync(inventory, From);
                return;
            }
            inventory.ActiveSlotIndex = m_activeSlotIndex;
            long revision = NetworkStateRevisionTracker.AdvanceAuthoritativeRevision(
                inventory.Project,
                NetworkStateRevisionDomain.Inventory,
                inventory.Id);
            NetworkManager.Queue(new ActiveSlotChangePacket(inventory, revision));
            return;
        }
        if (m_revision < 0) {
            return;
        }
        NetworkStateRevisionDecision decision = NetworkStateRevisionTracker.Receive(
            inventory.Project,
            NetworkStateRevisionDomain.Inventory,
            inventory.Id,
            m_revision,
            forceRefresh: true);
        if (decision.Apply) {
            InventoryPacketHelpers.ApplyAuthoritativeActiveSlot(
                inventory,
                m_activeSlotIndex);
        }
    }
}

