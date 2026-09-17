var entity = Game.GameManager.Project.FindEntity(__ENTITY_ID__);
var fridge = entity?.FindComponent<WonderfulEra.ComponentRefrigerator>();
if (fridge == null) return "missing";
fridge.PowerOn = __POWER__;
return "ok";