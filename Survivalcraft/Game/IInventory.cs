using Engine;
using GameEntitySystem;

namespace Game {
    public interface IInventory {
        Project Project { get; }

        int SlotsCount { get; }

        int Id { get; }

        int VisibleSlotsCount { get; set; }

        int ActiveSlotIndex { get; set; }

        void SetSlotValue(int slotIndex, object obj);

        int GetSlotValue(int slotIndex);

        int GetSlotCount(int slotIndex);

        void OnSlotChange(int slotIndex);

        int GetSlotCapacity(int slotIndex, int value);

        int GetSlotProcessCapacity(int slotIndex, int value);

        void AddSlotItems(int slotIndex, int value, int count);

        bool AddNetSlotItems(int slotIndex, int value, int count);

        void ProcessSlotItems(IInventory sourceInventory, int sourceSlotIndex, int slotIndex, int value, int count, int processCount, out int processedValue, out int processedCount);

        void ProcessSlotItems(int slotIndex, int value, int count, int processCount, out int processedValue, out int processedCount);

        /// <returns>实际移除的数量</returns>
        int RemoveSlotItems(int slotIndex, int count);

        int RemoveNetSlotItems(int slotIndex, int count);

        void DropAllItems(Vector3 position);

        void DropSlotItems(int slotIndex, Vector3 position, Vector3 velocity);
    }
}