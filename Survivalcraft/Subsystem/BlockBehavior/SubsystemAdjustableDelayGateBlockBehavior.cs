using Game.NetWork;
using Game.NetWork.Packages;

namespace Game {
    public class SubsystemAdjustableDelayGateBlockBehavior : SubsystemBlockBehavior {
        public override int[] HandledBlocks => [224];

        public override bool OnEditInventoryItem(IInventory inventory, int slotIndex, ComponentPlayer componentPlayer) {
            int value = inventory.GetSlotValue(slotIndex);
            int count = inventory.GetSlotCount(slotIndex);
            int data = Terrain.ExtractData(value);
            int delay = AdjustableDelayGateBlock.GetDelay(data);
            DialogsManager.ShowDialog(
                componentPlayer.GuiWidget,
                new EditAdjustableDelayGateDialog(
                    delay,
                    delegate(int newDelay) {
                        int data2 = AdjustableDelayGateBlock.SetDelay(data, newDelay);
                        int num = Terrain.ReplaceData(value, data2);
                        var p = new EditableBlockPackage(EditableType.AdjustableDelayGate, default, true, inventory.Id, slotIndex, newDelay);
                        NetworkManager.QueuePackage(p);
                        if (NetworkManager.WorkType != WorkType.Client) {
                            p.Handle(ProjectNet.Project, false);
                        }
                    }
                )
            );
            return true;
        }

        public override bool OnEditBlock(int x, int y, int z, int value, ComponentPlayer componentPlayer) {
            int data = Terrain.ExtractData(value);
            int delay = AdjustableDelayGateBlock.GetDelay(data);
            DialogsManager.ShowDialog(
                componentPlayer.GuiWidget,
                new EditAdjustableDelayGateDialog(
                    delay,
                    delegate(int newDelay) {
                        int num = AdjustableDelayGateBlock.SetDelay(data, newDelay);
                        if (num != data) {
                            int face = ((AdjustableDelayGateBlock)BlocksManager.Blocks[AdjustableDelayGateBlock.Index]).GetFace(value);
                            var cell = new CellFace(x, y, z, face);
                            var p = new EditableBlockPackage(EditableType.AdjustableDelayGate, cell, false, default, default, newDelay);
                            NetworkManager.QueuePackage(p);
                            if (NetworkManager.WorkType != WorkType.Client) {
                                p.Handle(ProjectNet.Project, false);
                            }
                        }
                    }
                )
            );
            return true;
        }
    }
}

