using Game.NetWork;
using Game.NetWork.Packages;

namespace Game {
    public class SubsystemButtonBlockBehavior : SubsystemBlockBehavior {
        public override int[] HandledBlocks => [142];

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
                            var p = new EditableBlockPackage(EditableType.Button, default, true, inventory.Id, slotIndex, newVoltageLevel);
                            NetworkManager.QueuePackage(p);
                            if (NetworkManager.WorkType != NetWork.WorkType.Client) {
                                p.Handle(false);
                            }
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
                            var p = new EditableBlockPackage(EditableType.Button, cell, false, default, default, newVoltageLevel);
                            NetworkManager.QueuePackage(p);
                            if (NetworkManager.WorkType != NetWork.WorkType.Client) {
                                p.Handle(false);
                            }
                        }
                    }
                )
            );
            return true;
        }
    }
}

