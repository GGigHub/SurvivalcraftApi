var entity = Game.GameManager.Project.FindEntity({0});
var furnace = entity?.FindComponent<Game.ComponentFurnace>();
if (furnace == null) return "missing";

furnace.StopSmelting(true);
furnace.m_smeltingRecipe = null;
furnace.m_updateSmeltingRecipe = true;
for (int i = 0; i < furnace.SlotsCount; i++) {
    furnace.RemoveSlotItems(i, furnace.GetSlotCount(i));
    furnace.m_slots[i].Value = 0;
    furnace.m_slots[i].Count = 0;
}

furnace.AddSlotItems(0, Game.Terrain.MakeBlockValue(Game.RawMeatBlock.Index), 4);
furnace.AddSlotItems(furnace.FuelSlotIndex, Game.Terrain.MakeBlockValue(Game.CoalChunkBlock.Index), 1);
furnace.m_fireTimeRemaining = 0f;
furnace.m_heatLevel = 0f;
furnace.UpdateSmeltingRecipe();
furnace.m_smeltingProgress = 0.35f;
return "started";
