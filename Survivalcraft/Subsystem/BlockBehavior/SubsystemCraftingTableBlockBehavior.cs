using Engine;

using TemplatesDatabase;

namespace Game {
    public class SubsystemCraftingTableBlockBehavior : SubsystemEntityBlockBehavior {
        public override int[] HandledBlocks => [27];

        public override void Load(ValuesDictionary valuesDictionary) {
            base.Load(valuesDictionary);
            m_databaseObject = Project.GameDatabase.Database.FindDatabaseObject("CraftingTable", Project.GameDatabase.EntityTemplateType, true);
        }

        public override bool InteractBlockEntity(ComponentBlockEntity blockEntity, ComponentMiner componentMiner) {
            if (blockEntity != null
                && componentMiner.ComponentPlayer != null) {
                ComponentCraftingTable componentCraftingTable = blockEntity.Entity.FindComponent<ComponentCraftingTable>(true);
                componentMiner.ComponentPlayer.ComponentGui.ModalPanelWidget = new CraftingTableWidget(
                    componentMiner.Inventory,
                    componentCraftingTable
                );
                AudioManager.PlaySound("Audio/UI/ButtonClick", 1f, 0f, 0f);
                return true;
            }
            return false;
        }

        public override bool OnInteract(TerrainRaycastResult raycastResult, ComponentMiner componentMiner) {
            if (NetworkManager.IsClientRunning && IsMainPlayer?.Invoke(componentMiner.ComponentPlayer) == true) {
                InvokeOnOpenInventoryRequest(new Point3(raycastResult.CellFace.X, raycastResult.CellFace.Y, raycastResult.CellFace.Z));
                return true;
            }
            return base.OnInteract(raycastResult, componentMiner);
        }
    }
}


