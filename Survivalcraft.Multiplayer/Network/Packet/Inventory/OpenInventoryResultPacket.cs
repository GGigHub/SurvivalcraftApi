using Engine;
using GameEntitySystem;

namespace Game.Network;

public sealed class OpenInventoryResultPacket : Packet {
    public override byte ID => (byte)PacketType.OpenInventoryResult;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;

    public long m_requestId;
    public NetworkContainerOpenRequestKind m_requestKind;
    public NetworkContainerOpenResultCode m_result;
    public int m_inventoryId;

    public OpenInventoryResultPacket() {
    }

    public OpenInventoryResultPacket(
        long requestId,
        NetworkContainerOpenRequestKind requestKind,
        NetworkContainerOpenResultCode result,
        int inventoryId) {
        m_requestId = requestId;
        m_requestKind = requestKind;
        m_result = result;
        m_inventoryId = inventoryId;
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_requestId);
        archive.Enum(ref m_requestKind);
        archive.Enum(ref m_result);
        archive.Value(ref m_inventoryId);
    }

    public override void Handle(bool isServer) {
        if (isServer
            || m_requestId <= 0
            || !Enum.IsDefined(m_requestKind)
            || !Enum.IsDefined(m_result)
            || m_result == NetworkContainerOpenResultCode.None
            || !ContainerOpenResultTracker.TryReserve(m_requestId)) {
            return;
        }
        NetworkContainerOpenResultCode result = m_result;
        bool opened = result == NetworkContainerOpenResultCode.Accepted
            && TryOpenInventory(m_inventoryId);
        if (result == NetworkContainerOpenResultCode.Accepted && !opened) {
            result = NetworkContainerOpenResultCode.ClientStateUnavailable;
        }
        ContainerOpenResultTracker.Complete(new NetworkContainerOpenResult(
            m_requestId,
            m_requestKind,
            result,
            m_inventoryId,
            opened));
    }

    public static bool IsSupportedInventory(IInventory inventory) =>
        inventory is ComponentChest
            or ComponentFurnace
            or ComponentDispenser
            or ComponentCraftingTable;

    public static void Queue(
        ClientSession to,
        long requestId,
        NetworkContainerOpenRequestKind requestKind,
        NetworkContainerOpenResultCode result,
        int inventoryId = 0) {
        if (to == null) {
            return;
        }
        NetworkManager.Queue(new OpenInventoryResultPacket(
            requestId,
            requestKind,
            result,
            inventoryId) {
            To = to
        });
    }

    private static bool TryOpenInventory(int inventoryId) {
        Project project = GameManager.Project;
        IInventory inventory = project?.FindSubsystem<SubsystemInventories>()
            ?.GetInventoryById(inventoryId);
        ComponentPlayer player = project?.FindSubsystem<SubsystemPlayers>()
            ?.ComponentPlayers
            .FirstOrDefault(candidate =>
                candidate.PlayerData.PlayerIndex == NetworkManager.PlayerIndex);
        if (inventory == null
            || player?.ComponentGui == null
            || player.ComponentMiner?.Inventory == null) {
            return false;
        }
        if (inventory is ComponentChest chest) {
            player.ComponentGui.ModalPanelWidget = new ChestWidget(
                player.ComponentMiner.Inventory,
                chest);
        }
        else if (inventory is ComponentFurnace furnace) {
            player.ComponentGui.ModalPanelWidget = new FurnaceWidget(
                player.ComponentMiner.Inventory,
                furnace);
        }
        else if (inventory is ComponentDispenser dispenser) {
            player.ComponentGui.ModalPanelWidget = new DispenserWidget(
                player.ComponentMiner.Inventory,
                dispenser);
        }
        else if (inventory is ComponentCraftingTable craftingTable) {
            player.ComponentGui.ModalPanelWidget = new CraftingTableWidget(
                player.ComponentMiner.Inventory,
                craftingTable);
        }
        else {
            return false;
        }
        AudioManager.PlaySound("Audio/UI/ButtonClick", 1f, 0f, 0f);
        return true;
    }
}
