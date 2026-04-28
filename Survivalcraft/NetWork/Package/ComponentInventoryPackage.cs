using Engine;
using GameEntitySystem;

namespace Game.NetWork.Packages;

public class ComponentInventoryPackage : IPackage {
    public override byte ID => (byte)PackageType.Inventory;

    public enum EventType {
        ActiveSlotChange,
        InventorySync,
        QueryErrorInventoryInfo,
        FurnaceSync,
        SetSlotsItem,
        SetInventory,
        HandleMoveItem,
        HandleDragDrop,
    }

    public class Slot {
        public int slotIndex;
        public byte type;
        public ComponentInventoryBase.Slot SlotItem;
        public List<int> clothingList;
    }

    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;
    public EventType m_eventType;

    public int m_inventory_id;
    public int m_activeSlot;

    public InventorySlot m_sourceInventorySlot;
    public InventorySlot m_targetInventorySlot;
    public DragMode m_dragMode;
    public bool m_processingOnly;

    public Dictionary<int, List<Slot>> m_slots;
    public Dictionary<IInventory, List<int>> m_syncItems;

    public ComponentInventoryPackage() {
    }

    public ComponentInventoryPackage(IInventory inventory, int activeSlot) {
        m_inventory_id = inventory.Id;
        m_activeSlot = activeSlot;
        m_eventType = EventType.ActiveSlotChange;
    }

    public ComponentInventoryPackage(int inventory_id, EventType eventType) {
        m_inventory_id = inventory_id;
        m_eventType = eventType;
    }

    public ComponentInventoryPackage(InventorySlot sourceInventorySlot, InventorySlot targetInventorySlot, EventType type) {
        m_sourceInventorySlot = sourceInventorySlot;
        m_targetInventorySlot = targetInventorySlot;
        m_eventType = type;
    }

    public ComponentInventoryPackage(InventorySlot inventorySlot) {
        m_sourceInventorySlot = inventorySlot;
        m_eventType = EventType.SetSlotsItem;
    }

    public ComponentInventoryPackage(Dictionary<IInventory, List<int>> syncItems) {
        m_slots = [];
        m_syncItems = [];
        m_eventType = EventType.InventorySync;

        foreach (var c in syncItems) {
            if (!m_slots.TryGetValue(c.Key.Id, out var slots)) {
                slots = [];
                m_slots.Add(c.Key.Id, slots);
            }
            if (c.Key is ComponentClothing clothing) {
                foreach (var index in c.Value) {
                    Slot slot = new Slot();
                    slot.type = 1;
                    slot.slotIndex = index;
                    slot.clothingList = clothing.GetClothes((ClothingSlot)index).ToList();
                    var tmp = slots.Find(x => x.slotIndex == index);
                    if (tmp == null) {
                        slots.Add(slot);
                    }
                    else {
                        int p = slots.IndexOf(tmp);
                        slots[p] = slot;
                    }
                }
            }
            else {
                foreach (var index in c.Value) {
                    Slot slot = new Slot();
                    slot.type = 0;
                    slot.slotIndex = index;
                    slot.SlotItem = new ComponentInventoryBase.Slot { Count = c.Key.GetSlotCount(index), Value = c.Key.GetSlotValue(index) };
                    var tmp = slots.Find(x => x.slotIndex == index);
                    if (tmp == null) {
                        slots.Add(slot);
                    }
                    else {
                        int p = slots.IndexOf(tmp);
                        slots[p] = slot;
                    }
                }
            }
        }
    }

    public override void Serialize(PackageSerializer archive) {
        archive.Enum(ref m_eventType);
        switch (m_eventType) {
            case EventType.QueryErrorInventoryInfo:
                archive.Value(ref m_inventory_id);
                break;
            case EventType.ActiveSlotChange:
                archive.Value(ref m_inventory_id);
                archive.Value(ref m_activeSlot);
                break;
            case EventType.InventorySync:
                SerializeInventorySync(archive);
                break;
            case EventType.HandleMoveItem:
                SerializeInventorySlot(archive, ref m_sourceInventorySlot);
                SerializeInventorySlot(archive, ref m_targetInventorySlot);
                break;
            case EventType.HandleDragDrop:
                SerializeInventorySlot(archive, ref m_sourceInventorySlot, includeValueCount: false);
                archive.Enum(ref m_dragMode);
                SerializeInventorySlot(archive, ref m_targetInventorySlot, includeValueCount: false);
                archive.Value(ref m_processingOnly);
                break;
            case EventType.SetSlotsItem:
                SerializeInventorySlot(archive, ref m_sourceInventorySlot);
                break;
        }
    }

    private void SerializeInventorySync(PackageSerializer archive) {
        int inventoryCount = archive.IsWriting ? m_slots.Count : 0;
        archive.Value(ref inventoryCount);
        if (archive.IsReading) {
            m_slots = [];
            for (int i = 0; i < inventoryCount; i++) {
                int inventoryId = 0;
                archive.Value(ref inventoryId);
                int listCount = 0;
                archive.Value(ref listCount);
                List<Slot> slots = [];
                for (int k = 0; k < listCount; k++) {
                    Slot slot = new Slot();
                    archive.Value(ref slot.slotIndex);
                    archive.Value(ref slot.type);
                    if (slot.type == 0) {
                        int count = 0;
                        int value = 0;
                        archive.Value(ref count);
                        archive.Value(ref value);
                        slot.SlotItem = new ComponentInventoryBase.Slot { Count = count, Value = value };
                    }
                    else {
                        slot.clothingList = [];
                        byte clothingCount = 0;
                        archive.Value(ref clothingCount);
                        for (int j = 0; j < clothingCount; j++) {
                            int clothingItem = 0;
                            archive.Value(ref clothingItem);
                            slot.clothingList.Add(clothingItem);
                        }
                    }
                    slots.Add(slot);
                }
                m_slots.Add(inventoryId, slots);
            }
        }
        else {
            foreach (var item in m_slots) {
                int inventoryId = item.Key;
                archive.Value(ref inventoryId);
                int listCount = item.Value.Count;
                archive.Value(ref listCount);
                for (int k = 0; k < item.Value.Count; k++) {
                    Slot slot = item.Value[k];
                    archive.Value(ref slot.slotIndex);
                    archive.Value(ref slot.type);
                    if (slot.type == 0) {
                        int count = slot.SlotItem.Count;
                        int value = slot.SlotItem.Value;
                        archive.Value(ref count);
                        archive.Value(ref value);
                    }
                    else {
                        byte clothingCount = (byte)slot.clothingList.Count;
                        archive.Value(ref clothingCount);
                        for (int j = 0; j < slot.clothingList.Count; j++) {
                            int clothingItem = slot.clothingList[j];
                            archive.Value(ref clothingItem);
                        }
                    }
                }
            }
        }
    }

    private void SerializeInventorySlot(PackageSerializer archive, ref InventorySlot slot, bool includeValueCount = true) {
        slot ??= new InventorySlot();
        archive.Value(ref slot.inventoryId);
        archive.Value(ref slot.slotIndex);
        if (includeValueCount) {
            archive.Value(ref slot.value);
            archive.Value(ref slot.count);
        }
    }

    public override void Handle(bool isServer) {
        var subsystemInventories = GameManager.Project.FindSubsystem<SubsystemInventories>();

        IInventory sourceInventoryObject = null;
        IInventory targetInventoryObject = null;

        switch (m_eventType) {
            case EventType.ActiveSlotChange:
                subsystemInventories.FindInventoryById(m_inventory_id, m_inventory => {
                    if (isServer) {
                        var entity = (m_inventory as Component)?.Entity;
                        var player = entity?.FindComponent<ComponentPlayer>();
                        if (player != null && From != null && player.PlayerGuid == From.PlayerGuid && IsSlotIndexValid(m_inventory, m_activeSlot)) {
                            m_inventory.ActiveSlotIndex = m_activeSlot;
                            Except = From;
                            NetworkManager.QueuePackage(this);
                        }
                    }
                    else {
                        m_inventory.ActiveSlotIndex = m_activeSlot;
                    }
                });
                break;
            case EventType.InventorySync:
                if (!isServer) {
                    foreach (var item in m_slots) {
                        subsystemInventories.FindInventoryById(item.Key, inventory => {
                            foreach (var item2 in item.Value) {
                                if (item2.type == 0) {
                                    inventory.SetSlotValue(item2.slotIndex, item2.SlotItem);
                                }
                                else {
                                    inventory.SetSlotValue(item2.slotIndex, item2.clothingList);
                                }
                            }
                        });
                    }
                }
                break;
            case EventType.QueryErrorInventoryInfo:
                if (isServer) {
                    subsystemInventories.FindInventoryById(m_inventory_id, m_inventory => {
                        string extra = "";
                        if (m_inventory is ComponentCraftingTable t) {
                            extra = t.Entity.ValuesDictionary.DatabaseObject.Name;
                        }
                        Log.Information($"璇锋眰閿欒鐨勮儗鍖?ID [{m_inventory_id}]锛屾潵鑷?[{m_inventory.GetType().Name}][{extra}]");
                    });
                }
                break;
            case EventType.HandleMoveItem:
                sourceInventoryObject = subsystemInventories.GetInventoryById(m_sourceInventorySlot.inventoryId);
                targetInventoryObject = subsystemInventories.GetInventoryById(m_targetInventorySlot.inventoryId);
                if (sourceInventoryObject != null && targetInventoryObject != null) {
                    bool reject = false;
                    if (isServer) {
                        if (IsOwnedByOtherPlayer(sourceInventoryObject, From, out _)) reject = true;
                        if (IsOwnedByOtherPlayer(targetInventoryObject, From, out _)) reject = true;
                        if (!IsSlotIndexValid(sourceInventoryObject, m_sourceInventorySlot.slotIndex)) reject = true;
                        if (!IsSlotIndexValid(targetInventoryObject, m_targetInventorySlot.slotIndex)) reject = true;
                        if (m_targetInventorySlot.count < 0) reject = true;
                        if (sourceInventoryObject is ComponentCreativeInventory && NetworkManager.GetEffectiveGameMode(From.PlayerData) != GameMode.Creative) {
                            NetworkManager.Server.RemoveClient(From.PlayerData.Client, "闈炴硶鎿嶄綔: 鐢熷瓨妯″紡浣跨敤鍒涢€犵墿鍝?");
                            reject = true;
                        }
                    }

                    if (isServer && reject) {
                        m_sourceInventorySlot.value = sourceInventoryObject.GetSlotValue(m_sourceInventorySlot.slotIndex);
                        m_sourceInventorySlot.count = sourceInventoryObject.GetSlotCount(m_sourceInventorySlot.slotIndex);
                        m_targetInventorySlot.value = targetInventoryObject.GetSlotValue(m_targetInventorySlot.slotIndex);
                        m_targetInventorySlot.count = targetInventoryObject.GetSlotCount(m_targetInventorySlot.slotIndex);
                        if (From != null) {
                            ComponentInventoryPackage package = new ComponentInventoryPackage(m_sourceInventorySlot) { To = From };
                            ComponentInventoryPackage package2 = new ComponentInventoryPackage(m_targetInventorySlot) { To = From };
                            NetworkManager.QueuePackage(package);
                            NetworkManager.QueuePackage(package2);
                        }
                        return;
                    }
                    var dummyWidget = new InventorySlotWidget();
                    dummyWidget.HandleMoveItem(sourceInventoryObject, m_sourceInventorySlot.slotIndex, targetInventoryObject, m_targetInventorySlot.slotIndex, m_targetInventorySlot.count);
                }
                break;
            case EventType.HandleDragDrop:
                sourceInventoryObject = subsystemInventories.GetInventoryById(m_sourceInventorySlot.inventoryId);
                targetInventoryObject = subsystemInventories.GetInventoryById(m_targetInventorySlot.inventoryId);
                if (sourceInventoryObject != null && targetInventoryObject != null) {
                    bool reject = false;
                    if (isServer) {
                        if (IsOwnedByOtherPlayer(sourceInventoryObject, From, out _)) reject = true;
                        if (IsOwnedByOtherPlayer(targetInventoryObject, From, out _)) reject = true;
                        if (!IsSlotIndexValid(sourceInventoryObject, m_sourceInventorySlot.slotIndex)) reject = true;
                        if (!IsSlotIndexValid(targetInventoryObject, m_targetInventorySlot.slotIndex)) reject = true;
                        if (sourceInventoryObject is ComponentCreativeInventory && NetworkManager.GetEffectiveGameMode(From.PlayerData) != GameMode.Creative) {
                            NetworkManager.Server.RemoveClient(From.PlayerData.Client, "闈炴硶鎿嶄綔: 鐢熷瓨妯″紡浣跨敤鍒涢€犵墿鍝?");
                            reject = true;
                        }
                    }

                    if (isServer && reject) {
                        m_sourceInventorySlot.value = sourceInventoryObject.GetSlotValue(m_sourceInventorySlot.slotIndex);
                        m_sourceInventorySlot.count = sourceInventoryObject.GetSlotCount(m_sourceInventorySlot.slotIndex);
                        m_targetInventorySlot.value = targetInventoryObject.GetSlotValue(m_targetInventorySlot.slotIndex);
                        m_targetInventorySlot.count = targetInventoryObject.GetSlotCount(m_targetInventorySlot.slotIndex);
                        if (From != null) {
                            ComponentInventoryPackage package = new ComponentInventoryPackage(m_sourceInventorySlot) { To = From };
                            ComponentInventoryPackage package2 = new ComponentInventoryPackage(m_targetInventorySlot) { To = From };
                            NetworkManager.QueuePackage(package);
                            NetworkManager.QueuePackage(package2);
                        }
                        return;
                    }
                    var dragDropWidget = new InventorySlotWidget();
                    dragDropWidget.HandleDragDrop(sourceInventoryObject, m_sourceInventorySlot.slotIndex, m_dragMode, targetInventoryObject, m_targetInventorySlot.slotIndex, m_processingOnly);
                }
                break;
            case EventType.SetSlotsItem:
                if (!isServer) {
                    sourceInventoryObject = subsystemInventories.GetInventoryById(m_sourceInventorySlot.inventoryId);
                    if (sourceInventoryObject is ComponentInventoryBase componentInventoryBase) {
                        ComponentInventoryBase.Slot slot = new ComponentInventoryBase.Slot();
                        slot.Value = m_sourceInventorySlot.value;
                        slot.Count = m_sourceInventorySlot.count;
                        componentInventoryBase.SetSlotValue(m_sourceInventorySlot.slotIndex, slot);
                    }
                }
                break;
        }
    }

    public static bool IsOwnedByOtherPlayer(IInventory inv, Client from, out ComponentPlayer owner) {
        owner = null;
        if (inv is Component comp) {
            owner = comp.Entity?.FindComponent<ComponentPlayer>();
            if (owner != null && from != null) {
                return owner.PlayerGuid != from.PlayerGuid;
            }
        }
        return false;
    }

    public static bool IsSlotIndexValid(IInventory inv, int index) {
        return inv != null && index >= 0 && index < inv.SlotsCount;
    }
}

public class InventorySlot {
    public int inventoryId;
    public int slotIndex;
    public int value;
    public int count;
}
