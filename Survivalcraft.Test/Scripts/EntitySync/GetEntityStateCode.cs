int targetId = __ENTITY_ID__;
var entity = Game.GameManager.Project.FindEntity(targetId);
if (entity == null) {
    return "missing";
}
var body = entity.FindComponent<Game.ComponentBody>();
var damage = entity.FindComponent<Game.ComponentDamage>();
var boat = entity.FindComponent<Game.ComponentBoat>();
var position = body != null ? body.Position : Engine.Vector3.Zero;
float hitpoints = damage != null ? damage.Hitpoints : 0f;
return string.Format(
    System.Globalization.CultureInfo.InvariantCulture,
    "id={0};boat={1};body={2};damage={3};pos={4:R},{5:R},{6:R};hitpoints={7:R}",
    entity.Id,
    boat != null ? 1 : 0,
    body != null ? 1 : 0,
    damage != null ? 1 : 0,
    position.X,
    position.Y,
    position.Z,
    hitpoints);
