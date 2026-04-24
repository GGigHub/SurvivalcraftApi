using Engine;
using Game.NetWork;
using Game.NetWork.Packages;

namespace Game {
    public class SubsystemTruthTableCircuitBlockBehavior : SubsystemEditableItemBehavior<TruthTableData> {
        public override int[] HandledBlocks => [188];

        public SubsystemTruthTableCircuitBlockBehavior() : base(188) { }

        public override bool OnEditInventoryItem(IInventory inventory, int slotIndex, ComponentPlayer componentPlayer) {
            if (componentPlayer.DragHostWidget.IsDragInProgress) {
                return false;
            }
            int value = inventory.GetSlotValue(slotIndex);
            int count = inventory.GetSlotCount(slotIndex);
            int id = Terrain.ExtractData(value);
            TruthTableData truthTableData = GetItemData(id);
            truthTableData = truthTableData != null ? (TruthTableData)truthTableData.Copy() : new TruthTableData();
            DialogsManager.ShowDialog(
                componentPlayer.GuiWidget,
                new EditTruthTableDialog(
                    truthTableData,
                    delegate {
                        var p = new EditableBlockPackage(default, true, inventory.Id, slotIndex, truthTableData);
                        NetworkManager.QueuePackage(p);
                        if (NetworkManager.WorkType != WorkType.Client) {
                            p.Handle(false);
                        }
                    }
                )
            );
            return true;
        }

        public override bool OnEditBlock(int x, int y, int z, int value, ComponentPlayer componentPlayer) {
            TruthTableData truthTableData = GetBlockData(new Point3(x, y, z)) ?? new TruthTableData();
            DialogsManager.ShowDialog(
                componentPlayer.GuiWidget,
                new EditTruthTableDialog(
                    truthTableData,
                    delegate {
                        int face = ((TruthTableCircuitBlock)BlocksManager.Blocks[188]).GetFace(value);
                        var cell = new CellFace(x, y, z, face);
                        var p = new EditableBlockPackage(cell, false, default, default, truthTableData);
                        NetworkManager.QueuePackage(p);
                        if (NetworkManager.WorkType != WorkType.Client) {
                            p.Handle(false);
                        }
                    }
                )
            );
            return true;
        }
    }
}
