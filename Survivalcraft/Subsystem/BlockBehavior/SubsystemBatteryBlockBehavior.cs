using Game.NetWork;
using Game.NetWork.Packages;

namespace Game {
    public class SubsystemBatteryBlockBehavior : SubsystemBlockBehavior {
        public override int[] HandledBlocks => [138];

        public override bool OnEditInventoryItem(IInventory inventory, int slotIndex, ComponentPlayer componentPlayer) {
            if (componentPlayer.DragHostWidget.IsDragInProgress) {
                return false;
            }
            int value = inventory.GetSlotValue(slotIndex);
            int count = inventory.GetSlotCount(slotIndex);
            int data = Terrain.ExtractData(value);
            int voltageLevel = BatteryBlock.GetVoltageLevel(data);
            DialogsManager.ShowDialog(
                componentPlayer.GuiWidget,
                new EditVoltageLevelDialog(
                    voltageLevel,
                    delegate(int newVoltageLevel) {
                        int data2 = BatteryBlock.SetVoltageLevel(data, newVoltageLevel);
                        int num = Terrain.ReplaceData(value, data2);
                        if (num != value) {
                            var p = new EditableBlockPackage(EditableItemType.Battery, default, true, inventory.Id, slotIndex, newVoltageLevel);
                            CommonLib.Net.QueuePackage(p);
                            if (CommonLib.WorkType != WorkType.Client) {
                                p.Handle(ProjectNet.Project, CommonLib.Net, false);
                            }
                        }
                    }
                )
            );
            return true;
        }

        public override bool OnEditBlock(int x, int y, int z, int value, ComponentPlayer componentPlayer) {
            int data = Terrain.ExtractData(value);
            int voltageLevel = BatteryBlock.GetVoltageLevel(data);
            DialogsManager.ShowDialog(
                componentPlayer.GuiWidget,
                new EditVoltageLevelDialog(
                    voltageLevel,
                    delegate(int newVoltageLevel) {
                        int num = BatteryBlock.SetVoltageLevel(data, newVoltageLevel);
                        if (num != data) {
                            int face = 4;
                            var cell = new CellFace(x, y, z, face);
                            var p = new EditableBlockPackage(EditableItemType.Battery, cell, false, default, default, newVoltageLevel);
                            CommonLib.Net.QueuePackage(p);
                            if (CommonLib.WorkType != WorkType.Client) {
                                p.Handle(ProjectNet.Project, CommonLib.Net, false);
                            }
                        }
                    }
                )
            );
            return true;
        }
    }
}