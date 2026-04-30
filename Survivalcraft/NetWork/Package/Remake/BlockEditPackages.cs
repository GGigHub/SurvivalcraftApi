using Engine;

namespace Game.NetWork.Packages;

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

public class CrossbowPullPackage : IPackage {
    public override byte ID => (byte)PackageType.CrossbowPull;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;

    public int m_inventoryId;
    public int m_slotIndex;

    public CrossbowPullPackage() {
    }

    public CrossbowPullPackage(IInventory inventory, int slotIndex) {
        m_inventoryId = inventory.Id;
        m_slotIndex = slotIndex;
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_inventoryId);
        archive.Value(ref m_slotIndex);
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            var subsystemInventories = GameManager.Project.FindSubsystem<SubsystemInventories>();
            IInventory inventory = subsystemInventories.GetInventoryById(m_inventoryId);
            if (inventory != null) {
                int theItemValue = inventory.GetSlotValue(m_slotIndex);
                if (Terrain.ExtractContents(theItemValue) == 200) { // Crossbow
                    int data = Terrain.ExtractData(theItemValue);
                    int value = Terrain.MakeBlockValue(200, 0, CrossbowBlock.SetDraw(data, 15));
                    inventory.RemoveSlotItems(m_slotIndex, 1);
                    inventory.AddSlotItems(m_slotIndex, value, 1);
                }
            }
        }
    }
}

public class EditSignPackage : IPackage {
    public override byte ID => (byte)PackageType.EditSign;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;

    public Point3 m_point3;

    public EditSignPackage() {
    }

    public EditSignPackage(Point3 point) {
        m_point3 = point;
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Value(ref m_point3);
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            To = From;
            NetworkManager.QueuePackage(this);
        }
        else {
            DialogsManager.ShowDialog(NetworkManager.MainPlayer.GuiWidget, new EditSignDialog(GameManager.Project.FindSubsystem<SubsystemSignBlockBehavior>(), m_point3));
        }
    }
}
