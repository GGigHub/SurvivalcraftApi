float spawnX = __POS_X__f;
float spawnY = __POS_Y__f;
float spawnZ = __POS_Z__f;
var project = Game.GameManager.Project;
if (project == null) {
    return "no-project";
}

var entity = DatabaseManager.CreateEntity(project, "__ENTITY_NAME__", false);
if (entity == null) {
    return "no-template";
}
var saghoul = entity.FindComponent<SAGhoul.Components.ComponentSAGhouls>();
if (saghoul == null) {
    return "no-saghoul-component";
}
((Game.ComponentFrame)saghoul.m_componentCreature.ComponentBody).Position = new Engine.Vector3(spawnX, spawnY, spawnZ);
saghoul.PersistentAttack = true;
saghoul.IsBoss = __IS_BOSS__;
project.AddEntity(entity);
return "spawned:" + entity.Id;