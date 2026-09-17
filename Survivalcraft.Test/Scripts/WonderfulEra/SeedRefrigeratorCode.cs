var entity = Game.GameManager.Project.FindEntity(__ENTITY_ID__);
var fridge = entity?.FindComponent<WonderfulEra.ComponentRefrigerator>();
if (fridge == null) return "missing";
int apple = Game.BlocksManager.GetBlockIndex<WonderfulEra.AppleBlock>(false, false);
fridge.AddSlotItems(0, Game.Terrain.MakeBlockValue(apple), 8);
return "seeded";