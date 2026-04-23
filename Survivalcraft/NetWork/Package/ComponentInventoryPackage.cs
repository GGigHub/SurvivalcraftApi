using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Engine;
using GameEntitySystem;

namespace Game.NetWork.Packages;

public class ComponentInventoryPackage : IPackage
{

    public byte ID => (byte)PackageType.ComponentInventory;

    public enum EventType
    {
        ActiveSlotChange,                           // 活动槽变更，拿物品的那几个主界面槽位
        InventorySync,                                // 同步背包

        // 下面 2 个可以考虑删掉，但暂时先保留
        QueryErrorInventoryInfo,
        FurnaceSync,
        // =============================

        SetSlotsItem,                                   // 璁剧疆鑳屽寘鍐呯殑鐗╁搧
        SetInventory,                                   // 璁剧疆鏁翠釜鑳屽寘
        HandleMoveItem,                             // InventorySlotWidget 涓殑澶勭悊鍑芥暟鍚屽悕
        HandleDragDrop,                              // InventorySlotWidget 涓殑澶勭悊鍑芥暟鍚屽悕
    }
    public class Slot
    {
        public int slotIndex;
        public byte type;
        public ComponentInventoryBase.Slot SlotItem;
        public List<int> clothingList;
    }


    public Client To { get; set; }
    public Client Except { get; set; }
    public Client From { get; set; }
    public ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public EventType m_eventType;

    public int m_inventory_id;
    public int m_activeSlot;


    public InventorySlot m_sourceInventorySlot;
    public InventorySlot m_targetInventorySlot;
    public DragMode m_dragMode;
    public bool m_processingOnly;



    public Dictionary<int, List<Slot>> m_slots;
    public Dictionary<IInventory, List<int>> m_syncItems;

    public ComponentInventoryPackage()
    {
    }
    public ComponentInventoryPackage(IInventory inventory, int activeSlot)
    {
        m_inventory_id = inventory.Id;
        m_activeSlot = activeSlot;
        m_eventType = EventType.ActiveSlotChange;
    }

    public ComponentInventoryPackage(int inventory_id, EventType eventType)
    {
        m_inventory_id = inventory_id;
        m_eventType = eventType;
    }

    public ComponentInventoryPackage(InventorySlot sourceInventorySlot, InventorySlot targetInventorySlot, EventType type)
    {
        m_sourceInventorySlot = sourceInventorySlot;
        m_targetInventorySlot = targetInventorySlot;
        m_eventType = type;
    }

    public ComponentInventoryPackage(InventorySlot inventorySlot)
    {
        m_sourceInventorySlot = inventorySlot;
        m_eventType = EventType.SetSlotsItem;
    }

    // 客户端/服务器发送包后，相关 Inventory 的 Generation 加 1。
    // 服务端收到包后，判断 Inventory 的 Generation 是否为本地值 +1，验证失败则驳回并强制同步服务端数据。
    // isLostSync 如果为 true，则返回所有格子的完整数据。
    public ComponentInventoryPackage(Dictionary<IInventory, List<int>> syncItems)
    {
        m_slots = new Dictionary<int, List<Slot>>();
        m_syncItems = new Dictionary<IInventory, List<int>>();
        m_eventType = EventType.InventorySync;

        foreach (var c in syncItems)
        {
            if (!m_slots.TryGetValue(c.Key.Id, out var slots))
            {
                slots = new List<Slot>();
                m_slots.Add(c.Key.Id, slots);
            }
            if (c.Key is ComponentClothing clothing)
            {
                foreach (var index in c.Value)
                {
                    Slot slot = new Slot();
                    slot.type = 1;
                    slot.slotIndex = index;
                    slot.clothingList = clothing.GetClothes((ClothingSlot)index).ToList();
                    var tmp = slots.Find(x => x.slotIndex == index);
                    if (tmp == null) slots.Add(slot);
                    else
                    {
                        int p = slots.IndexOf(tmp);
                        slots[p] = slot;
                    }
                }
            }
            else
            {
                foreach (var index in c.Value)
                {
                    Slot slot = new Slot();
                    slot.type = 0;
                    slot.slotIndex = index;
                    slot.SlotItem = new ComponentInventoryBase.Slot() { Count = c.Key.GetSlotCount(index), Value = c.Key.GetSlotValue(index) };
                    var tmp = slots.Find(x => x.slotIndex == index);
                    if (tmp == null) slots.Add(slot);
                    else
                    {
                        int p = slots.IndexOf(tmp);
                        slots[p] = slot;
                    }
                }
            }
        }
    }

    public void WriteData(PackageStreamWriter writer)
    {
        writer.WriteEnum(m_eventType);
        switch (m_eventType)
        {
            case EventType.QueryErrorInventoryInfo:
                writer.Write(m_inventory_id);
                break;
            case EventType.ActiveSlotChange:
                writer.Write(m_inventory_id);
                writer.Write(m_activeSlot);
                break;
            case EventType.InventorySync:
                writer.Write(m_slots.Count);
                foreach (var item in m_slots)
                {
                    writer.Write(item.Key);//inventory id
                    writer.Write(item.Value.Count);//list count
                    foreach (var item2 in item.Value)
                    {
                        writer.Write(item2.slotIndex);
                        writer.Write(item2.type);
                        if (item2.type == 0)
                        {
                            writer.Write(item2.SlotItem.Count);
                            writer.Write(item2.SlotItem.Value);
                        }
                        else
                        {
                            writer.Write((byte)item2.clothingList.Count);
                            foreach (var item3 in item2.clothingList)
                            {
                                writer.Write(item3);
                            }

                        }
                    }
                }
                break;
            case EventType.HandleMoveItem:
                writer.Write(m_sourceInventorySlot.inventoryId);
                writer.Write(m_sourceInventorySlot.slotIndex);
                writer.Write(m_targetInventorySlot.inventoryId);
                writer.Write(m_targetInventorySlot.slotIndex);
                writer.Write(m_targetInventorySlot.count);
                break;
            case EventType.HandleDragDrop:
                writer.Write(m_sourceInventorySlot.inventoryId);
                writer.Write(m_sourceInventorySlot.slotIndex);
                writer.WriteEnum(m_dragMode);
                writer.Write(m_targetInventorySlot.inventoryId);
                writer.Write(m_targetInventorySlot.slotIndex);
                writer.Write(m_processingOnly);
                break;
            case EventType.SetSlotsItem:
                writer.Write(m_sourceInventorySlot.inventoryId);
                writer.Write(m_sourceInventorySlot.slotIndex);
                writer.Write(m_sourceInventorySlot.value);
                writer.Write(m_sourceInventorySlot.count);
                break;
        }
    }
    public void ReadData(PackageStreamReader reader)
    {
        m_eventType = reader.ReadEnum<EventType>();
        switch (m_eventType)
        {
            case EventType.QueryErrorInventoryInfo:
                m_inventory_id = reader.ReadInt32();
                break;
            case EventType.ActiveSlotChange:
                m_inventory_id = reader.ReadInt32();
                m_activeSlot = reader.ReadInt32();
                break;
            case EventType.InventorySync:
                m_slots = new Dictionary<int, List<Slot>>();
                int count = reader.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    int inventoryId = reader.ReadInt32();
                    int listCount = reader.ReadInt32();
                    List<Slot> slots = new List<Slot>();
                    for (int k = 0; k < listCount; k++)
                    {
                        Slot slot = new Slot();
                        slot.slotIndex = reader.ReadInt32();
                        slot.type = reader.ReadByte();
                        if (slot.type == 0)
                        {
                            slot.SlotItem = new ComponentInventoryBase.Slot() { Count = reader.ReadInt32(), Value = reader.ReadInt32() };
                        }
                        else
                        {
                            slot.clothingList = new List<int>();
                            int listCount2 = reader.ReadByte();
                            for (int j = 0; j < listCount2; j++)
                            {
                                slot.clothingList.Add(reader.ReadInt32());
                            }
                        }
                        slots.Add(slot);
                    }
                    m_slots.Add(inventoryId, slots);
                }
                break;


            case EventType.HandleMoveItem:
                m_sourceInventorySlot = new InventorySlot();
                m_sourceInventorySlot.inventoryId = reader.ReadInt32();
                m_sourceInventorySlot.slotIndex = reader.ReadInt32();
                m_targetInventorySlot = new InventorySlot();
                m_targetInventorySlot.inventoryId = reader.ReadInt32();
                m_targetInventorySlot.slotIndex = reader.ReadInt32();
                m_targetInventorySlot.count = reader.ReadInt32();
                break;
            case EventType.HandleDragDrop:
                m_sourceInventorySlot = new InventorySlot();
                m_sourceInventorySlot.inventoryId = reader.ReadInt32();
                m_sourceInventorySlot.slotIndex = reader.ReadInt32();
                m_dragMode = reader.ReadEnum<DragMode>();
                m_targetInventorySlot = new InventorySlot();
                m_targetInventorySlot.inventoryId = reader.ReadInt32();
                m_targetInventorySlot.slotIndex = reader.ReadInt32();
                m_processingOnly = reader.ReadBoolean();
                break;
            case EventType.SetSlotsItem:
                {
                    m_sourceInventorySlot = new InventorySlot();
                    m_sourceInventorySlot.inventoryId = reader.ReadInt32();
                    m_sourceInventorySlot.slotIndex = reader.ReadInt32();
                    m_sourceInventorySlot.value = reader.ReadInt32();
                    m_sourceInventorySlot.count = reader.ReadInt32();
                }
                break;
        }
    }

    public void Handle(ProjectNet projectNet, bool isServer)
    {
        var subsystemInventories = projectNet.FindSubsystem<SubsystemInventories>();

        IInventory sourceInventoryObject = null;
        IInventory targetInventoryObject = null;

        switch (m_eventType)
        {
            case EventType.ActiveSlotChange:
                subsystemInventories.FindInventoryById(m_inventory_id, (m_inventory) =>
                {
                    if (isServer)
                    {
                        var entity = (m_inventory as Component)?.Entity;
                        var player = entity?.FindComponent<ComponentPlayer>();
                        if (player != null && From != null && player.PlayerGuid == From.PlayerGuid && IsSlotIndexValid(m_inventory, m_activeSlot))
                        {
                            m_inventory.ActiveSlotIndex = m_activeSlot;
                            Except = From;
                            NetworkManager.QueuePackage(this);
                        }

                    }
                    else
                    {
                        m_inventory.ActiveSlotIndex = m_activeSlot;
                    }
                });
                break;
            case EventType.InventorySync:
                if (isServer)
                {
                    // 服务端不接受来自客户端的同步，乱发可以直接踢出服务器。
                }
                else
                {
                    // 客户端接收服务端的同步包
                    foreach (var item in m_slots)
                    {
                        subsystemInventories.FindInventoryById(item.Key, (inventory) =>
                        {
                            foreach (var item2 in item.Value)
                            {

                                if (item2.type == 0)
                                {
                                    inventory.SetSlotValue(item2.slotIndex, item2.SlotItem);
                                }
                                else
                                {
                                    inventory.SetSlotValue(item2.slotIndex, item2.clothingList);
                                }
                            }
                        });

                    }
                }
                break;
            case EventType.QueryErrorInventoryInfo:
                if (isServer)
                {
                    subsystemInventories.FindInventoryById(m_inventory_id, (m_inventory) =>
                    {
                        string extra = "";
                        if (m_inventory is ComponentCraftingTable t)
                        {
                            extra = t.Entity.ValuesDictionary.DatabaseObject.Name;
                        }
                        Log.Information($"请求错误的背包 ID [{m_inventory_id}]，来自 [{m_inventory.GetType().Name}][{extra}]");
                    });
                }
                break;

            case EventType.HandleMoveItem:
                sourceInventoryObject = subsystemInventories.GetInventoryById(m_sourceInventorySlot.inventoryId);
                targetInventoryObject = subsystemInventories.GetInventoryById(m_targetInventorySlot.inventoryId);
                if (sourceInventoryObject != null && targetInventoryObject != null)
                {
                    bool reject = false;
                    if (isServer)
                    {
                        if (IsOwnedByOtherPlayer(sourceInventoryObject, From, out var srcOwner)) reject = true;
                        if (IsOwnedByOtherPlayer(targetInventoryObject, From, out var dstOwner)) reject = true;
                        if (!IsSlotIndexValid(sourceInventoryObject, m_sourceInventorySlot.slotIndex)) reject = true;
                        if (!IsSlotIndexValid(targetInventoryObject, m_targetInventorySlot.slotIndex)) reject = true;
                        if (m_targetInventorySlot.count < 0) reject = true;
                        if (sourceInventoryObject is ComponentCreativeInventory && NetworkManager.GetEffectiveGameMode(From.PlayerData) != GameMode.Creative)
                        {
                            NetworkManager.Server.RemoveClient(From.PlayerData.Client, "非法操作: 生存模式使用创造物品");
                            reject = true;
                        }
                    }
                    // 鏁版嵁鎹曟崏

                    if (isServer && reject)
                    {
                        m_sourceInventorySlot.value = sourceInventoryObject.GetSlotValue(m_sourceInventorySlot.slotIndex);
                        m_sourceInventorySlot.count = sourceInventoryObject.GetSlotCount(m_sourceInventorySlot.slotIndex);
                        m_targetInventorySlot.value = targetInventoryObject.GetSlotValue(m_targetInventorySlot.slotIndex);
                        m_targetInventorySlot.count = targetInventoryObject.GetSlotCount(m_targetInventorySlot.slotIndex);
                        if (From != null)
                        {
                            ComponentInventoryPackage package = new ComponentInventoryPackage(m_sourceInventorySlot);
                            package.To = From;
                            ComponentInventoryPackage package2 = new ComponentInventoryPackage(m_targetInventorySlot);
                            package2.To = From;
                            NetworkManager.QueuePackage(package);
                            NetworkManager.QueuePackage(package2);
                        }
                        return;
                    }
                    var dummyWidget = new InventorySlotWidget();
                    dummyWidget.HandleMoveItem(sourceInventoryObject, m_sourceInventorySlot.slotIndex, targetInventoryObject, m_targetInventorySlot.slotIndex, m_targetInventorySlot.count);
                }
                else
                {
                    // 服务端找不到背包？怀疑是来打服的！！

                }
                break;
            case EventType.HandleDragDrop:
                sourceInventoryObject = subsystemInventories.GetInventoryById(m_sourceInventorySlot.inventoryId);
                targetInventoryObject = subsystemInventories.GetInventoryById(m_targetInventorySlot.inventoryId);
                if (sourceInventoryObject != null && targetInventoryObject != null)
                {
                    bool reject = false;
                    if (isServer)
                    {
                        if (IsOwnedByOtherPlayer(sourceInventoryObject, From, out var srcOwner2)) reject = true;
                        if (IsOwnedByOtherPlayer(targetInventoryObject, From, out var dstOwner2)) reject = true;
                        if (!IsSlotIndexValid(sourceInventoryObject, m_sourceInventorySlot.slotIndex)) reject = true;
                        if (!IsSlotIndexValid(targetInventoryObject, m_targetInventorySlot.slotIndex)) reject = true;
                        if (sourceInventoryObject is ComponentCreativeInventory && NetworkManager.GetEffectiveGameMode(From.PlayerData) != GameMode.Creative)
                        {
                            NetworkManager.Server.RemoveClient(From.PlayerData.Client, "非法操作: 生存模式使用创造物品");
                            reject = true;
                        }
                    }
                    // 鏁版嵁鎹曟崏

                    if (isServer && reject)
                    {
                        m_sourceInventorySlot.value = sourceInventoryObject.GetSlotValue(m_sourceInventorySlot.slotIndex);
                        m_sourceInventorySlot.count = sourceInventoryObject.GetSlotCount(m_sourceInventorySlot.slotIndex);
                        m_targetInventorySlot.value = targetInventoryObject.GetSlotValue(m_targetInventorySlot.slotIndex);
                        m_targetInventorySlot.count = targetInventoryObject.GetSlotCount(m_targetInventorySlot.slotIndex);
                        if (From != null)
                        {
                            ComponentInventoryPackage package = new ComponentInventoryPackage(m_sourceInventorySlot);
                            package.To = From;
                            ComponentInventoryPackage package2 = new ComponentInventoryPackage(m_targetInventorySlot);
                            package2.To = From;
                            NetworkManager.QueuePackage(package);
                            NetworkManager.QueuePackage(package2);
                        }
                        return;
                    }
                    var dragDropWidget = new InventorySlotWidget();
                    dragDropWidget.HandleDragDrop(sourceInventoryObject, m_sourceInventorySlot.slotIndex, m_dragMode, targetInventoryObject, m_targetInventorySlot.slotIndex, m_processingOnly);
                }
                else
                {
                    // 服务端找不到背包？怀疑是来打服的！！

                }
                break;
            case EventType.SetSlotsItem:
                if (isServer)
                {
                    // 客户端要求服务端改背包？？这简直是造反！！

                }
                else
                {
                    sourceInventoryObject = subsystemInventories.GetInventoryById(m_sourceInventorySlot.inventoryId);
                    if (sourceInventoryObject != null && sourceInventoryObject is ComponentInventoryBase componentInventoryBase)
                    {
                        ComponentInventoryBase.Slot slot = new ComponentInventoryBase.Slot();
                        slot.Value = m_sourceInventorySlot.value;
                        slot.Count = m_sourceInventorySlot.count;
                        componentInventoryBase.SetSlotValue(m_sourceInventorySlot.slotIndex, slot);
                    }
                }
                break;
        }
    }
    public static bool IsOwnedByOtherPlayer(IInventory inv, Client from, out ComponentPlayer owner)
    {
        owner = null;
        if (inv is Component comp)
        {
            owner = comp.Entity?.FindComponent<ComponentPlayer>();
            if (owner != null && from != null)
            {
                return owner.PlayerGuid != from.PlayerGuid;
            }
        }
        return false;
    }

    public static bool IsSlotIndexValid(IInventory inv, int index)
    {
        return inv != null && index >= 0 && index < inv.SlotsCount;
    }
}

public class InventorySlot
{
    public int inventoryId;
    public int slotIndex;
    public int value;
    public int count;
}
