using Engine;
using GameEntitySystem;

namespace Game {
    public interface IInventory {
        // Keep the original IInventory contract abstract. Legacy assemblies
        // implement these members and must continue to satisfy the interface.
        Project Project { get; }

        int SlotsCount { get; }

        int VisibleSlotsCount { get; set; }

        int ActiveSlotIndex { get; set; }

        int GetSlotValue(int slotIndex);

        int GetSlotCount(int slotIndex);

        int GetSlotCapacity(int slotIndex, int value);

        int GetSlotProcessCapacity(int slotIndex, int value);

        void AddSlotItems(int slotIndex, int value, int count);

        void ProcessSlotItems(int slotIndex, int value, int count, int processCount, out int processedValue, out int processedCount);

        /// <returns>实际移除的数量</returns>
        int RemoveSlotItems(int slotIndex, int count);

        void DropAllItems(Vector3 position);

        // These members were added after the original IInventory contract.
        // Default implementations keep older mod assemblies loadable.
        int Id => Project?.FindSubsystem<SubsystemInventories>()?.GetOrCreateId(this) ?? 0;

        void SetSlotValue(int slotIndex, object obj) {
        }

        void OnSlotChange(int slotIndex) {
        }

        bool AddNetSlotItems(int slotIndex, int value, int count) {
            int oldValue = GetSlotValue(slotIndex);
            int oldCount = GetSlotCount(slotIndex);
            AddSlotItems(slotIndex, value, count);
            return oldValue != GetSlotValue(slotIndex) || oldCount != GetSlotCount(slotIndex);
        }

        void ProcessSlotItems(
            IInventory sourceInventory,
            int sourceSlotIndex,
            int slotIndex,
            int value,
            int count,
            int processCount,
            out int processedValue,
            out int processedCount
        ) {
            ProcessSlotItems(slotIndex, value, count, processCount, out processedValue, out processedCount);
        }

        int RemoveNetSlotItems(int slotIndex, int count) {
            return RemoveSlotItems(slotIndex, count);
        }

        void DropSlotItems(int slotIndex, Vector3 position, Vector3 velocity) {
            ComponentInventoryBase.DropSlotItems(this, slotIndex, position, velocity);
        }
    }
}
