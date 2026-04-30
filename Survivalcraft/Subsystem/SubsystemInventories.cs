using Game.NetWork;
using GameEntitySystem;

namespace Game;

public class SubsystemInventories : Subsystem {
    private static Dictionary<IInventory, List<int>> syncItems = new Dictionary<IInventory, List<int>>();

    private Dictionary<int, IInventory> inventories = new Dictionary<int, IInventory>();


    public int ProduceInventoryId(IInventory inventory) {
        for (int i = 1; i < int.MaxValue; i++) {
            if (!inventories.ContainsKey(i)) {
                inventories.Add(i, inventory);
                return i;
            }
        }
        throw new Exception("新增Inventory失败");
    }

    public int RegisterInventory(IInventory inventory) {
        if (!inventories.ContainsKey(inventory.Id)) {
            inventories.Add(inventory.Id, inventory);
            return inventory.Id;
        }
        else {
            if (NetworkManager.WorkType == NetWork.WorkType.Client) {
                inventories[inventory.Id] = inventory;
                return inventory.Id;
            }
            else {
                int id = ProduceInventoryId(inventory);
                return id;
            }
        }
    }

    public bool FindInventoryById(int id, Action<IInventory> action = null) {
        if (inventories.TryGetValue(id, out var inventory)) {
            action?.Invoke(inventory);
            return true;
        }
#if DEBUG
        Engine.Log.Information($"Inventory id not found: {id}");
#endif
        return false;
    }

    public IInventory GetInventoryById(int id) {
        if (inventories.TryGetValue(id, out var inventory)) {
            return inventory;
        }
        return null;
    }

    public static void pushSyncItem(IInventory inventory, int slotIndex) {
        if (!syncItems.TryGetValue(inventory, out var list)) {
            list = new List<int>();
            syncItems.Add(inventory, list);
        }
        if (!list.Contains(slotIndex)) list.Add(slotIndex);
    }
    public static void flushSyncItems() {
        if (syncItems.Count > 0) {
            if (NetworkManager.WorkType == NetWork.WorkType.Server) {
                foreach (var item in syncItems.Keys) {
                    NetworkManager.QueuePackage(new NetWork.Packages.InventorySyncPackage(item));
                }
            }
            syncItems.Clear();
        }
    }
    public override void OnEntityRemoved(Entity entity) {
        foreach (IInventory i in entity.FindComponents<IInventory>()) {
            inventories.Remove(i.Id);
        }
    }
}
