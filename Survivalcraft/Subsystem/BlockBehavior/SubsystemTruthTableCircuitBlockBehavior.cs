using Engine;


namespace Game {
    public class SubsystemTruthTableCircuitBlockBehavior : SubsystemEditableItemBehavior<TruthTableData> {
        public override int[] HandledBlocks => [188];

        public SubsystemTruthTableCircuitBlockBehavior() : base(188) { }

        public static event Action<SubsystemTruthTableCircuitBlockBehavior, int, int, TruthTableData> OnEditItemData;

        public static event Action<SubsystemTruthTableCircuitBlockBehavior, CellFace, TruthTableData> OnEditBlockData;

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
                        if (!NetworkManager.IsClientRunning) {
                            int data = StoreItemDataAtUniqueId(truthTableData);
                            int value2 = Terrain.ReplaceData(value, data);
                            inventory.RemoveSlotItems(slotIndex, count);
                            inventory.AddSlotItems(slotIndex, value2, count);
                        }
                        OnEditItemData?.Invoke(this, inventory.Id, slotIndex, truthTableData);
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
                        if (!NetworkManager.IsClientRunning) {
                            SetBlockData(new Point3(x, y, z), truthTableData);
                            SubsystemElectricity subsystemElectricity = SubsystemTerrain.Project.FindSubsystem<SubsystemElectricity>(true);
                            ElectricElement electricElement = subsystemElectricity.GetElectricElement(x, y, z, face);
                            if (electricElement != null) {
                                subsystemElectricity.QueueElectricElementForSimulation(electricElement, subsystemElectricity.CircuitStep + 1);
                            }
                        }
                        OnEditBlockData?.Invoke(this, cell, truthTableData);
                    }
                )
            );
            return true;
        }
    }
}


