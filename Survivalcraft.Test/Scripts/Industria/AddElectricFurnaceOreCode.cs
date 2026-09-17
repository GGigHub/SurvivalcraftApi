var project = Game.GameManager.Project;
if (project == null) return "no-project";
var blockEntity = project.FindSubsystem<Game.SubsystemBlockEntities>().GetBlockEntity(@@0@@, @@1@@, @@2@@);
if (blockEntity == null) return "no-block-entity";
var furnace = blockEntity.Entity.FindComponent<SCIENEW.ComponentElectricFurnace>();
if (furnace == null) return "no-electric-furnace";
var inventory = furnace as Game.IInventory;
for (int i = 0; i < inventory.SlotsCount; i++)
{
    inventory.RemoveSlotItems(i, inventory.GetSlotCount(i));
}
furnace.m_updateSmeltingRecipe = true;
inventory.AddSlotItems(0, Game.Terrain.MakeBlockValue(Game.IronOreBlock.Index), 40);
return "added";