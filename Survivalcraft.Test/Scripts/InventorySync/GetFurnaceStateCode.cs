var entity = Game.GameManager.Project.FindEntity({0});
var furnace = entity?.FindComponent<Game.ComponentFurnace>();
if (furnace == null) return "missing";
return string.Format(
    System.Globalization.CultureInfo.InvariantCulture,
    "{0:R}|{1:R}|{2:R}",
    furnace.m_fireTimeRemaining,
    furnace.m_smeltingProgress,
    furnace.m_heatLevel);
