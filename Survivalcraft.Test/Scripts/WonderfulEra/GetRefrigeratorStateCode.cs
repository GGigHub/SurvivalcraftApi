var entity = Game.GameManager.Project.FindEntity(__ENTITY_ID__);
var fridge = entity?.FindComponent<WonderfulEra.ComponentRefrigerator>();
if (fridge == null) return "missing";
return string.Format(
    System.Globalization.CultureInfo.InvariantCulture,
    "{0}|{1}|{2}|{3}|{4}",
    fridge.SlotsCount,
    Game.Terrain.ExtractContents(fridge.GetSlotValue(0)),
    fridge.GetSlotCount(0),
    fridge.PowerOn ? 1 : 0,
    fridge.m_powerOn ? 1 : 0);