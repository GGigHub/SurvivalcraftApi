var project = Game.GameManager.Project;
if (project == null) return "no-project";
var blockEntity = project.FindSubsystem<Game.SubsystemBlockEntities>().GetBlockEntity(@@0@@, @@1@@, @@2@@);
if (blockEntity == null) return "no-block-entity";
var furnace = blockEntity.Entity.FindComponent<SCIENEW.ComponentElectricFurnace>();
if (furnace == null) return "no-electric-furnace";
var inventory = furnace as Game.IInventory;
var slots = new string[inventory.SlotsCount];
for (int i = 0; i < inventory.SlotsCount; i++)
{
    slots[i] = string.Format("{0}:{1}", inventory.GetSlotValue(i), inventory.GetSlotCount(i));
}
return string.Format("entity={0};fire={1:R};progress={2:R};heat={3:R};recipe={4};slots=[{5}]",
    blockEntity.Entity.Id,
    furnace.m_fireTimeRemaining,
    furnace.m_smeltingProgress,
    furnace.m_heatLevel,
    furnace.m_smeltingRecipe != null,
    string.Join(",", slots));