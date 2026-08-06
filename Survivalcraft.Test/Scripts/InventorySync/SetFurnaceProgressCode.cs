var entity = Game.GameManager.Project.FindEntity({0});
var furnace = entity?.FindComponent<Game.ComponentFurnace>();
if (furnace == null) return false;

furnace.m_smeltingProgress = {1};
return true;
