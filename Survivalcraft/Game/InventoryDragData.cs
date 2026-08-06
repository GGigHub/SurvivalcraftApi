namespace Game {
    public class InventoryDragData {
        public IInventory Inventory;

        public int SlotIndex;

        public DragMode DragMode;

        // The quantity calculated when the drag starts. Keeping it with the
        // drag data prevents drop targets from reinterpreting the drag mode.
        public int Count;
    }
}
