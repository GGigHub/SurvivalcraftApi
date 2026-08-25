
using GameEntitySystem;

namespace Game;

public class SubsystemInventories : Subsystem {
    private readonly Dictionary<int, IInventory> inventories = new Dictionary<int, IInventory>();

    // Legacy inventories do not have an Id field. Keep their assigned IDs by
    // object identity so custom Equals/GetHashCode implementations cannot alias
    // two different inventory instances.
    private readonly Dictionary<IInventory, int> inventoryIds = new(ReferenceEqualityComparer.Instance);


    public int ProduceInventoryId(IInventory inventory) {
        return GetOrCreateId(inventory);
    }

    internal int GetOrCreateId(IInventory inventory) {
        if (inventory == null) {
            throw new ArgumentNullException(nameof(inventory));
        }
        if (inventoryIds.TryGetValue(inventory, out int existingId)) {
            return existingId;
        }

        for (int i = 1; i < int.MaxValue; i++) {
            if (!inventories.ContainsKey(i)) {
                inventories.Add(i, inventory);
                inventoryIds.Add(inventory, i);
                return i;
            }
        }
        throw new Exception("新增Inventory失败");
    }

    public int RegisterInventory(IInventory inventory) {
        if (inventory == null) {
            throw new ArgumentNullException(nameof(inventory));
        }
        if (inventoryIds.TryGetValue(inventory, out int existingId)) {
            return existingId;
        }

        int id = inventory.Id;
        // Reading Id on a legacy implementation uses the default interface
        // property above and may have registered the inventory already.
        if (inventoryIds.TryGetValue(inventory, out existingId)) {
            return existingId;
        }
        if (id <= 0) {
            return GetOrCreateId(inventory);
        }

        if (!inventories.ContainsKey(id)) {
            inventories.Add(id, inventory);
            inventoryIds.Add(inventory, id);
            return id;
        }
        else {
            if (NetworkManager.IsClientRunning) {
                inventories[id] = inventory;
                inventoryIds[inventory] = id;
                return id;
            }
            else {
                return ProduceInventoryId(inventory);
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
            if (inventoryIds.TryGetValue(i, out int id)) {
                inventories.Remove(id);
                inventoryIds.Remove(i);
            }
        }
    }

    public override void Dispose() {
        inventories.Clear();
        inventoryIds.Clear();
    }
}


