var entity = Game.GameManager.Project.FindEntity(__ENTITY_ID__);
if (entity == null) return "missing";
var health = entity.FindComponent<Game.ComponentHealth>();
var damage = entity.FindComponent<Game.ComponentDamage>();
var body = entity.FindComponent<Game.ComponentBody>();
var loco = entity.FindComponent<Game.ComponentLocomotion>();
return string.Format(
    System.Globalization.CultureInfo.InvariantCulture,
    "health={0:R};hitpoints={1:R};stun={2:R};px={3:R};py={4:R};pz={5:R};vx={6:R};vy={7:R};vz={8:R}",
    health?.Health ?? -1f,
    damage?.Hitpoints ?? -1f,
    loco?.StunTime ?? -1f,
    body?.Position.X ?? 0f,
    body?.Position.Y ?? 0f,
    body?.Position.Z ?? 0f,
    body?.Velocity.X ?? 0f,
    body?.Velocity.Y ?? 0f,
    body?.Velocity.Z ?? 0f);
