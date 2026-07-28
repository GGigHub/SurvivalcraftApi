

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
                    delegate (int newDelay) {
                        int data2 = AdjustableDelayGateBlock.SetDelay(data, newDelay);
                        int num = Terrain.ReplaceData(value, data2);
                        if (!NetworkManager.IsClientRunning) {
                            inventory.RemoveSlotItems(slotIndex, count);
                            inventory.AddSlotItems(slotIndex, num, count);
                        }
                        ModsManager.HookAction("OnNetworkAdjustableDelayGateItemEdited", loader => loader.OnNetworkAdjustableDelayGateItemEdited(this, inventory.Id, slotIndex, newDelay));
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
                            if (!NetworkManager.IsClientRunning) {
                                int newValue = Terrain.ReplaceData(value, num);
                                SubsystemTerrain.ChangeCell(x, y, z, newValue);
                            }
                            ModsManager.HookAction("OnNetworkAdjustableDelayGateBlockEdited", loader => loader.OnNetworkAdjustableDelayGateBlockEdited(this, cell, newDelay));
                        }
                    }
                )
            );
            return true;
        }
    }
}



