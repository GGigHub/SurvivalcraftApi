int entityId = __ENTITY_ID__;
var entity = Game.GameManager.Project.FindEntity(entityId);
if (entity == null) return FormattableString.Invariant($"{entityId}|missing_entity_null");

foreach (var behavior in entity.FindComponents<ComponentBehavior>()) {
    behavior.IsActive = false;
    var type = behavior.GetType();
    while (type != null) {
        var field = type.GetField("m_importanceLevel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        if (field != null && field.FieldType == typeof(float)) {
            field.SetValue(behavior, 0f);
            break;
        }
        type = type.BaseType;
    }
}

entity.FindComponent<ComponentBehaviorSelector>()?.m_behaviors.Clear();
entity.FindComponent<ComponentPathfinding>()?.Stop();
entity.ValuesDictionary.SetValue("TestCreatureBodyObservedFlyOrder", "");

var subsystemUpdate = Game.GameManager.Project.FindSubsystem<SubsystemUpdate>();
if (subsystemUpdate != null) {
    foreach (var updateable in entity.FindComponents<IUpdateable>().ToList()) {
        if (updateable is ComponentBody) continue;
        subsystemUpdate.RemoveUpdateable(updateable);
        subsystemUpdate.m_updateables.Remove(updateable);
        subsystemUpdate.m_sortedUpdateables.Remove(updateable);
    }
}

var body = entity.FindComponent<ComponentBody>();
if (body == null) return FormattableString.Invariant($"{entityId}|missing_body_null");

body.IsGravityEnabled = false;
body.IsGroundDragEnabled = false;
body.IsWaterDragEnabled = false;
body.IsSmoothRiseEnabled = false;
body.TerrainCollidable = false;
body.BodyCollidable = false;
body.FluidCollidable = false;
body.CanBePushedByOtherBodies = false;
body.Velocity = Engine.Vector3.Zero;

var locomotion = entity.FindComponent<ComponentLocomotion>();
if (locomotion != null) {
    locomotion.WalkOrder = null;
    locomotion.FlyOrder = null;
    locomotion.SwimOrder = null;
    locomotion.LookOrder = Engine.Vector2.Zero;
    locomotion.TurnOrder = Engine.Vector2.Zero;
    locomotion.LastFlyOrder = null;
}

System.Action<ComponentLocomotion, float> onLocomotionUpdate = (locomotionComponent, dt) => {
    if (locomotionComponent.Entity?.Id != entityId) return;
    Engine.Vector3? flyOrder = locomotionComponent.LastFlyOrder ?? locomotionComponent.FlyOrder;
    if (flyOrder.HasValue) {
        Engine.Vector3 value = flyOrder.Value;
        locomotionComponent.Entity.ValuesDictionary.SetValue(
            "TestCreatureBodyObservedFlyOrder",
            FormattableString.Invariant($"{value.X:R},{value.Y:R},{value.Z:R}"));
    }
};
ComponentLocomotion.OnLocomotionUpdate += onLocomotionUpdate;

return string.Empty;
