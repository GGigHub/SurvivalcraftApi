var entity = Game.GameManager.Project.FindEntity(__ENTITY_ID__);
if (entity == null) return "missing";
var blockEntity = entity.FindComponent<WonderfulEra.ComponentNewBlockEntity>();
if (blockEntity == null) return "no-block-entity";
int data = Game.Terrain.ExtractData(blockEntity.BlockValue);
data = WonderfulEra.KeyBlock.SetRunning(data, true);
blockEntity.BlockValue = Game.Terrain.ReplaceData(blockEntity.BlockValue, data);

var inventory = entity.FindComponent<WonderfulEra.ComponentVehicleInventory>();
if (inventory == null) return "no-vehicle-inventory";
int gas = Game.BlocksManager.GetBlockIndex<WonderfulEra.GasBlock>(false, false);
inventory.AddSlotItems(inventory.FuelSlotIndex, Game.Terrain.MakeBlockValue(gas), 10);
inventory.SetEnergyProcess(1f);
return "ok";