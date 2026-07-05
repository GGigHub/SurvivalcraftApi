

namespace Game {
    public class SubsystemButtonBlockBehavior : SubsystemBlockBehavior {
        public override int[] HandledBlocks => [142];

        public static event Action<SubsystemButtonBlockBehavior, int, int, int> OnEditItemVoltage;

        public static event Action<SubsystemButtonBlockBehavior, CellFace, int> OnEditBlockVoltage;

        public override bool OnEditInventoryItem(IInventory inventory, int slotIndex, ComponentPlayer componentPlayer) {
            if (componentPlayer.DragHostWidget.IsDragInProgress) {
                return false;
            }
            int value = inventory.GetSlotValue(slotIndex);
            int count = inventory.GetSlotCount(slotIndex);
            int data = Terrain.ExtractData(value);
            int voltageLevel = ButtonBlock.GetVoltageLevel(data);
            DialogsManager.ShowDialog(
                componentPlayer.GuiWidget,
                new EditVoltageLevelDialog(
                    voltageLevel,
                    delegate (int newVoltageLevel) {
                        int data2 = ButtonBlock.SetVoltageLevel(data, newVoltageLevel);
                        int num = Terrain.ReplaceData(value, data2);
                        if (num != value) {
                            if (!NetworkManager.IsNetworkMode) {
                                inventory.RemoveSlotItems(slotIndex, count);
                                inventory.AddSlotItems(slotIndex, num, count);
                            }
                            OnEditItemVoltage?.Invoke(this, inventory.Id, slotIndex, newVoltageLevel);
                        }
                    }
                )
            );
            return true;
        }

        public override bool OnEditBlock(int x, int y, int z, int value, ComponentPlayer componentPlayer) {
            int data = Terrain.ExtractData(value);
            int voltageLevel = ButtonBlock.GetVoltageLevel(data);
            DialogsManager.ShowDialog(
                componentPlayer.GuiWidget,
                new EditVoltageLevelDialog(
                    voltageLevel,
                    delegate (int newVoltageLevel) {
                        int num = ButtonBlock.SetVoltageLevel(data, newVoltageLevel);
                        if (num != data) {
                            int face = 4;
                            var cell = new CellFace(x, y, z, face);
                            if (!NetworkManager.IsNetworkMode) {
                                int newValue = Terrain.ReplaceData(value, num);
                                SubsystemTerrain.ChangeCell(x, y, z, newValue);
                            }
                            OnEditBlockVoltage?.Invoke(this, cell, newVoltageLevel);
                        }
                    }
                )
            );
            return true;
        }
    }
}



