int targetId = __ENTITY_ID__;
var entity = Game.GameManager.Project.FindEntity(targetId);
if (entity != null) {
    Game.GameManager.Project.RemoveEntity(entity, true);
    return targetId;
}
return 0;
