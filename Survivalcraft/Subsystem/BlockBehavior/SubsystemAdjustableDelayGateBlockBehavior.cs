using Game.Network;

namespace Game {
    public class SubsystemAdjustableDelayGateBlockBehavior : SubsystemBlockBehavior {
        public override int[] HandledBlocks => [224];

        public event Action<SubsystemAdjustableDelayGateBlockBehavior, int, int, int> OnEditItemDelay;
        public event Action<SubsystemAdjustableDelayGateBlockBehavior, CellFace, int> OnEditBlockDelay;

        public override bool OnEditInventoryItem(IInventory inventory, int slotIndex, ComponentPlayer componentPlayer) {
            int value = inventory.GetSlotValue(slotIndex);
            int count = inventory.GetSlotCount(slotIndex);
            int data = Terrain.ExtractData(value);
            int delay = AdjustableDelayGateBlock.GetDelay(data);
            DialogsManager.ShowDialog(
                componentPlayer.GuiWidget,
                new EditAdjustableDelayGateDialog(
                    delay,
                    delegate (int newDelay) {
                        int data2 = AdjustableDelayGateBlock.SetDelay(data, newDelay);
                        int num = Terrain.ReplaceData(value, data2);
                        if (GameManager.IsNetworkMode?.Invoke() != true) {
                            inventory.RemoveSlotItems(slotIndex, count);
                            inventory.AddSlotItems(slotIndex, num, count);
                        }
                        OnEditItemDelay?.Invoke(this, inventory.Id, slotIndex, newDelay);
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
                    delegate (int newDelay) {
                        int num = AdjustableDelayGateBlock.SetDelay(data, newDelay);
                        if (num != data) {
                            int face = ((AdjustableDelayGateBlock)BlocksManager.Blocks[AdjustableDelayGateBlock.Index]).GetFace(value);
                            var cell = new CellFace(x, y, z, face);
                            if (GameManager.IsNetworkMode?.Invoke() != true) {
                                int newValue = Terrain.ReplaceData(value, num);
                                SubsystemTerrain.ChangeCell(x, y, z, newValue);
                            }
                            OnEditBlockDelay?.Invoke(this, cell, newDelay);
                        }
                    }
                )
            );
            return true;
        }
    }
}



