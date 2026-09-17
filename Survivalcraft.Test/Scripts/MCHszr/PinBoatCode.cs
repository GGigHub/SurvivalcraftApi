var entity = Game.GameManager.Project.FindEntity(__ENTITY_ID__);
if (entity == null) return "missing";
var body = entity.FindComponent<Game.ComponentBody>();
if (body == null) return "no-body";
body.IsGravityEnabled = false;
body.IsSmoothRiseEnabled = false;
body.Velocity = Engine.Vector3.Zero;
body.Position = new Engine.Vector3(__X__f, __Y__f, __Z__f);
return "pinned";