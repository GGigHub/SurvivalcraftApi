var entity = Game.GameManager.Project.FindEntity({0});
var furnace = entity?.FindComponent<Game.ComponentFurnace>();
if (furnace == null) return "missing";

for (int i = 0; i < furnace.SlotsCount; i++) {
    furnace.RemoveSlotItems(i, furnace.GetSlotCount(i));
}

furnace.AddSlotItems(0, Game.Terrain.MakeBlockValue(Game.RawMeatBlock.Index), 4);
furnace.AddSlotItems(furnace.FuelSlotIndex, Game.Terrain.MakeBlockValue(Game.CoalChunkBlock.Index), 1);
int fuelValue = Game.Terrain.MakeBlockValue(Game.CoalChunkBlock.Index);
var fuelBlock = Game.BlocksManager.Blocks[Game.Terrain.ExtractContents(fuelValue)];
furnace.m_fireTimeRemaining = fuelBlock.GetFuelFireDuration(fuelValue);
furnace.m_heatLevel = fuelBlock.GetFuelHeatLevel(fuelValue);
furnace.UpdateSmeltingRecipe();
furnace.m_smeltingProgress = 0.35f;
return "started";
