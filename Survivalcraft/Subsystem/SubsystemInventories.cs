
using GameEntitySystem;

namespace Game;

public class SubsystemInventories : Subsystem {
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
            if (NetworkManager.IsNetworkMode) {
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

    public override void OnEntityRemoved(Entity entity) {
        foreach (IInventory i in entity.FindComponents<IInventory>()) {
            inventories.Remove(i.Id);
        }
    }
}


