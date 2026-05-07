using Engine;

namespace Game.NetWork;

public class OpenInventoryByIDPackage : IPackage {
    public override byte ID => (byte)PackageType.OpenInventoryByID;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;

    public int m_inventoryId;

    public OpenInventoryByIDPackage() {
    }

    public OpenInventoryByIDPackage(IInventory inventory) {
        m_inventoryId = inventory.Id;
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_inventoryId);
    }

    public override void Handle(bool isServer) {
        if (From == null) return;
        var subsystemInventories = GameManager.Project.FindSubsystem<SubsystemInventories>();
        if (isServer) {
            IInventory inventory = subsystemInventories.GetInventoryById(m_inventoryId);
            if (inventory != null) {
                OpenInventoryByIDPackage package = new OpenInventoryByIDPackage(inventory);
                package.To = From;
                NetworkManager.QueuePackage(package);
            }
        }
        else {
            IInventory inventory = subsystemInventories.GetInventoryById(m_inventoryId);
            if (inventory != null) {
                var player = NetworkManager.MainPlayer;
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