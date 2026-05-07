using Game.Network;

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
                    delegate (int newVoltageLevel) {
                        int data2 = BatteryBlock.SetVoltageLevel(data, newVoltageLevel);
                        int num = Terrain.ReplaceData(value, data2);
                        if (num != value) {
                            var p = new BatteryVoltagePacket(default, true, inventory.Id, slotIndex, newVoltageLevel);
                            NetworkManager.QueuePacket(p);
                            if (NetworkManager.WorkType != Network.WorkType.Client) {
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
            int voltageLevel = BatteryBlock.GetVoltageLevel(data);
            DialogsManager.ShowDialog(
                componentPlayer.GuiWidget,
                new EditVoltageLevelDialog(
                    voltageLevel,
                    delegate (int newVoltageLevel) {
                        int num = BatteryBlock.SetVoltageLevel(data, newVoltageLevel);
                        if (num != data) {
                            int face = 4;
                            var cell = new CellFace(x, y, z, face);
                            var p = new BatteryVoltagePacket(cell, false, default, default, newVoltageLevel);
                            NetworkManager.QueuePacket(p);
                            if (NetworkManager.WorkType != Network.WorkType.Client) {
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



