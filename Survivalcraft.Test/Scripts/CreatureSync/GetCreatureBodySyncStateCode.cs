int entityId = __ENTITY_ID__;
var entity = Game.GameManager.Project.FindEntity(entityId);
if (entity == null) return FormattableString.Invariant($"{entityId}|missing_entity_null");

var body = entity.FindComponent<ComponentBody>();
if (body == null) return FormattableString.Invariant($"{entityId}|missing_body_null");

var locomotion = entity.FindComponent<ComponentLocomotion>();
Engine.Vector2 lookAngles = locomotion?.LookAngles ?? Engine.Vector2.Zero;
Engine.Vector3? flyOrder = locomotion?.LastFlyOrder ?? locomotion?.FlyOrder;
string observedFlyOrder = entity.ValuesDictionary.GetValue<string>("TestCreatureBodyObservedFlyOrder", "");
string flyOrderText = !string.IsNullOrEmpty(observedFlyOrder)
    ? observedFlyOrder
    : flyOrder.HasValue
        ? FormattableString.Invariant($"{flyOrder.Value.X:R},{flyOrder.Value.Y:R},{flyOrder.Value.Z:R}")
        : "null";

return FormattableString.Invariant(
    $"{entityId}|pos={body.Position.X:R},{body.Position.Y:R},{body.Position.Z:R};rot={body.Rotation.X:R},{body.Rotation.Y:R},{body.Rotation.Z:R},{body.Rotation.W:R};vel={body.Velocity.X:R},{body.Velocity.Y:R},{body.Velocity.Z:R};look={lookAngles.X:R},{lookAngles.Y:R};fly={flyOrderText}");
