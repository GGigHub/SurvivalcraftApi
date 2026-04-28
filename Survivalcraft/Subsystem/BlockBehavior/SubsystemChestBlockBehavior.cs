using Engine;
using Game.NetWork;
using Game.NetWork.Packages;
using TemplatesDatabase;

namespace Game {
    public class SubsystemChestBlockBehavior : SubsystemEntityBlockBehavior {
        public override int[] HandledBlocks => [45];

        public override void Load(ValuesDictionary valuesDictionary) {
            base.Load(valuesDictionary);
            m_databaseObject = Project.GameDatabase.Database.FindDatabaseObject("Chest", Project.GameDatabase.EntityTemplateType, true);
        }

        public override bool InteractBlockEntity(ComponentBlockEntity blockEntity, ComponentMiner componentMiner) {
            if (blockEntity != null
                && componentMiner.ComponentPlayer != null) {
                ComponentChest componentChest = blockEntity.Entity.FindComponent<ComponentChest>(true);
                componentMiner.ComponentPlayer.ComponentGui.ModalPanelWidget = new ChestWidget(componentMiner.Inventory, componentChest);
                AudioManager.PlaySound("Audio/UI/ButtonClick", 1f, 0f, 0f);
                return true;
            }
            return false;
        }

        public override bool OnInteract(TerrainRaycastResult raycastResult, ComponentMiner componentMiner) {
            if (NetworkManager.WorkType == NetWork.WorkType.Client && NetworkManager.MainPlayer == componentMiner.ComponentPlayer) {
                IPackage package = new BlockEditPackage(new Point3(raycastResult.CellFace.X, raycastResult.CellFace.Y, raycastResult.CellFace.Z), BlockEditPackage.EventType.OpenInventoryByPoint);
                NetworkManager.QueuePackage(package);
                return true;
            }
            return base.OnInteract(raycastResult, componentMiner);
        }
    }
}
