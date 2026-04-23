using Engine;

namespace Game.NetWork.Packages;

public class BlockEditPackage : IPackage
{
    public byte ID => (byte)PackageType.BlockEdit;
    public Client To { get; set; }
    public Client Except { get; set; }
    public Client From { get; set; }

    public ClientStage MinNeedState => ClientStage.ProjectLoaded;

    public enum EventType
    {
        OpenInventoryByID,                  // 打开背包，通过背包 id。注：背包指的是 IInventory
        OpenInventoryByPoint,              // 打开背包，通过位置，通常是BlockEntity，例如箱子，炉子
        CrossbowPull,                            // 十字弩拉弦
        EditSign,                                     // 编辑牌子
    }

    public int m_inventoryId;
    public int m_slotIndex;

    public EventType m_type;

    public Point3 m_point3;

    public BlockEditPackage()
    {
    }

    public BlockEditPackage(IInventory inventory)
    {
        m_inventoryId = inventory.Id;
        m_type= EventType.OpenInventoryByID;
    }

    public BlockEditPackage(Point3 point3, EventType type)
    {
        m_point3= point3;
        m_type = type;
    }

    public BlockEditPackage(IInventory inventory, int slotIndex, EventType type)
    {
        m_inventoryId = inventory.Id;
        m_slotIndex = slotIndex;
        m_type = type;
    }

    public void WriteData(PackageStreamWriter writer)
    {
        writer.WriteEnum(m_type);
        switch(m_type)
        {
            case EventType.OpenInventoryByID:
                writer.Write(m_inventoryId);
                break;
            case EventType.OpenInventoryByPoint:
                writer.Write(m_point3);
                break;
            case EventType.CrossbowPull:
                writer.Write(m_inventoryId);
                writer.Write(m_slotIndex);
                break;
            case EventType.EditSign:
                writer.Write(m_point3);
                break;
        }
    }

    public void ReadData(PackageStreamReader reader)
    {
        m_type = reader.ReadEnum<EventType>();
        switch(m_type)
        {
            case EventType.OpenInventoryByID:
                m_inventoryId = reader.ReadInt32();
                break;
            case EventType.OpenInventoryByPoint:
                m_point3 = reader.ReadPoint3();
                break;
            case EventType.CrossbowPull:
                m_inventoryId = reader.ReadInt32();
                m_slotIndex = reader.ReadInt32();
                break;
            case EventType.EditSign:
                m_point3 = reader.ReadPoint3();
                break;
        }
    }

    public void Handle(ProjectNet projectNet, bool isServer)
    {
        if(From == null) // 会出现空么？？？？？
        {
            Log.Information("出现空玩家打开背包");
            return;
        }
        var subsystemInventories = projectNet.FindSubsystem<SubsystemInventories>();
        switch (m_type)
        {
            case EventType.OpenInventoryByID:
                if(isServer)
                {
                    IInventory inventory = subsystemInventories.GetInventoryById(m_inventoryId);
                    if (inventory != null)
                    {

                        IPackage package = new BlockEditPackage(inventory);
                        package.To = From;
                        NetworkManager.QueuePackage(package);
                    }
                }
                else
                {
                    IInventory inventory = subsystemInventories.GetInventoryById(m_inventoryId);
                    if(inventory != null)
                    {
                        var player = NetworkManager.MainPlayer;
                        // 箱子
                        if (inventory is ComponentChest componentChest)
                        {
                            player.ComponentGui.ModalPanelWidget = new ChestWidget(player.ComponentMiner.Inventory, componentChest);
                            AudioManager.PlaySound("Audio/UI/ButtonClick", 1f, 0f, 0f);
                        }
                        // 熔炉
                        else if(inventory is ComponentFurnace componentFurnace)
                        {
                            player.ComponentGui.ModalPanelWidget = new FurnaceWidget(player.ComponentMiner.Inventory, componentFurnace);
                            AudioManager.PlaySound("Audio/UI/ButtonClick", 1f, 0f, 0f);
                        }
                        // 发射器
                        else if(inventory is ComponentDispenser componentDispenser)
                        {
                            player.ComponentMiner.ComponentPlayer.ComponentGui.ModalPanelWidget = new DispenserWidget(player.ComponentMiner.Inventory, componentDispenser);
                            AudioManager.PlaySound("Audio/UI/ButtonClick", 1f, 0f, 0f);
                        }
                        // 工作台
                        else if(inventory is ComponentCraftingTable componentCraftingTable)
                        {
                            player.ComponentMiner.ComponentPlayer.ComponentGui.ModalPanelWidget = new CraftingTableWidget(player.ComponentMiner.Inventory, componentCraftingTable);
                            AudioManager.PlaySound("Audio/UI/ButtonClick", 1f, 0f, 0f);
                        }

                    }
                }
                break;
             case EventType.OpenInventoryByPoint:
                if(isServer)
                {
                    var subsystemBlockEntities = projectNet.FindSubsystem<SubsystemBlockEntities>();
                    ComponentBlockEntity blockEntity = subsystemBlockEntities.GetBlockEntity(m_point3.X, m_point3.Y, m_point3.Z);
                    if (blockEntity != null)
                    {
                        IInventory inventory = blockEntity.Entity.FindComponent<IInventory>(throwOnError: false);
                        if(inventory != null)
                        {

                            IPackage package = new BlockEditPackage(inventory);
                            package.To = From;
                            NetworkManager.QueuePackage(package);
                        }
                    }
                }
                break;
            case EventType.CrossbowPull:
                if(isServer)
                {
                    IInventory inventory = subsystemInventories.GetInventoryById(m_inventoryId);

                    int theItemValue = inventory.GetSlotValue(m_slotIndex);
                    if(Terrain.ExtractContents(theItemValue) == 200)
                    {
                        int data = Terrain.ExtractData(theItemValue);
                        int value = Terrain.MakeBlockValue(200, 0, CrossbowBlock.SetDraw(data, 15));
                        inventory.RemoveSlotItems(m_slotIndex, 1);
                        inventory.AddSlotItems(m_slotIndex, value, 1);
                    }
                }
                break;
            case EventType.EditSign:
                if(isServer == true)
                {

                    To = From;
                    NetworkManager.QueuePackage(this);
                }
                else
                {
                    DialogsManager.ShowDialog(NetworkManager.MainPlayer.GuiWidget, new EditSignDialog(projectNet.FindSubsystem<SubsystemSignBlockBehavior>(), m_point3));
                }
                break;
        }
    }
}
