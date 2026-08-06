var entity = Game.GameManager.Project.FindEntity({0});
var furnace = entity?.FindComponent<Game.ComponentFurnace>();
if (furnace == null) return "missing";

return string.Format(
    System.Globalization.CultureInfo.InvariantCulture,
    "{0}|{1}|{2}|{3}",
    furnace.GetSlotCount(0),
    furnace.GetSlotCount(furnace.FuelSlotIndex),
    furnace.GetSlotCount(furnace.ResultSlotIndex),
    furnace.GetSlotCount(furnace.RemainsSlotIndex));
