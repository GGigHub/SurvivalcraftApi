var entity = Game.GameManager.Project.FindEntity(__ENTITY_ID__);
if (entity == null) return "missing";
var damage = entity.FindComponent<Game.ComponentDamage>();
if (damage == null) return "no-damage";
damage.Hitpoints = __HITPOINTS__f;
damage.m_lastHitpoints = __HITPOINTS__f;
return "set";