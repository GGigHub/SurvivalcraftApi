using Engine;

namespace Game.Network;

public class OpenInventoryByIDPacket : Packet {
    public override byte ID => (byte)PacketType.OpenInventoryByID;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;

    public int m_inventoryId;

    public OpenInventoryByIDPacket() {
    }

    public OpenInventoryByIDPacket(IInventory inventory) {
        m_inventoryId = inventory.Id;
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_inventoryId);
    }

    public override void Handle(bool isServer) {
        if (From == null) return;
        var subsystemInventories = GameManager.Project.FindSubsystem<SubsystemInventories>();
        if (isServer) {
            IInventory inventory = subsystemInventories.GetInventoryById(m_inventoryId);
            if (inventory != null) {
                OpenInventoryByIDPacket packet = new OpenInventoryByIDPacket(inventory);
                packet.To = From;
                NetworkManager.Queue(packet);
            }
        }
        else {
            IInventory inventory = subsystemInventories.GetInventoryById(m_inventoryId);
            if (inventory != null) {
                var player = GameManager.Project.FindSubsystem<SubsystemPlayers>()?.ComponentPlayers.FirstOrDefault(p => p.PlayerData.PlayerIndex == NetworkManager.PlayerIndex);
                if (inventory is ComponentChest componentChest) {
                    player.ComponentGui.ModalPanelWidget = new ChestWidget(player.ComponentMiner.Inventory, componentChest);
                    AudioManager.PlaySound("Audio/UI/ButtonClick", 1f, 0f, 0f);
                }
                else if (inventory is ComponentFurnace componentFurnace) {
                    player.ComponentGui.ModalPanelWidget = new FurnaceWidget(player.ComponentMiner.Inventory, componentFurnace);
                    AudioManager.PlaySound("Audio/UI/ButtonClick", 1f, 0f, 0f);
                }
                else if (inventory is ComponentDispenser componentDispenser) {
                    player.ComponentMiner.ComponentPlayer.ComponentGui.ModalPanelWidget = new DispenserWidget(player.ComponentMiner.Inventory, componentDispenser);
                    AudioManager.PlaySound("Audio/UI/ButtonClick", 1f, 0f, 0f);
                }
                else if (inventory is ComponentCraftingTable componentCraftingTable) {
                    player.ComponentMiner.ComponentPlayer.ComponentGui.ModalPanelWidget = new CraftingTableWidget(player.ComponentMiner.Inventory, componentCraftingTable);
                    AudioManager.PlaySound("Audio/UI/ButtonClick", 1f, 0f, 0f);
                }
            }
        }
    }
}

