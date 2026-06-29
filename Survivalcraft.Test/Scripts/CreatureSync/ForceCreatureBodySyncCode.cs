int entityId = __ENTITY_ID__;
var entity = Game.GameManager.Project.FindEntity(entityId);
if (entity == null) return FormattableString.Invariant($"{entityId}|missing_entity_null");

var body = entity.FindComponent<ComponentBody>();
if (body == null) return FormattableString.Invariant($"{entityId}|missing_body_null");

var locomotion = entity.FindComponent<ComponentLocomotion>();
Engine.Vector3 forcedPosition = body.Position + new Engine.Vector3(1.75f, 0.5f, -1.25f);
Engine.Quaternion forcedRotation = Engine.Quaternion.Normalize(
    body.Rotation * Engine.Quaternion.CreateFromAxisAngle(Engine.Vector3.UnitY, 1.5707964f));
Engine.Vector3 forcedVelocity = new Engine.Vector3(0.37f, -0.12f, 0.29f);
Engine.Vector2 forcedLookAngles = new Engine.Vector2(0.31f, -0.27f);
Engine.Vector3 forcedFlyOrder = new Engine.Vector3(0.2f, 0.8f, -0.4f);

void DisableBehavior(GameEntitySystem.Entity currentEntity) {
    foreach (var behavior in currentEntity.FindComponents<ComponentBehavior>()) {
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
    currentEntity.FindComponent<ComponentBehaviorSelector>()?.m_behaviors.Clear();
    currentEntity.FindComponent<ComponentPathfinding>()?.Stop();
}

void ApplyForcedState() {
    var currentEntity = Game.GameManager.Project.FindEntity(entityId);
    if (currentEntity == null) return;
    DisableBehavior(currentEntity);

    var currentBody = currentEntity.FindComponent<ComponentBody>();
    if (currentBody != null) {
        currentBody.IsGravityEnabled = false;
        currentBody.IsGroundDragEnabled = false;
        currentBody.IsWaterDragEnabled = false;
        currentBody.IsSmoothRiseEnabled = false;
        currentBody.TerrainCollidable = false;
        currentBody.BodyCollidable = false;
        currentBody.FluidCollidable = false;
        currentBody.CanBePushedByOtherBodies = false;
        currentBody.Position = forcedPosition;
        currentBody.Rotation = forcedRotation;
        currentBody.Velocity = forcedVelocity;
    }

    var currentLocomotion = currentEntity.FindComponent<ComponentLocomotion>();
    if (currentLocomotion != null) {
        currentLocomotion.LookAngles = forcedLookAngles;
        currentLocomotion.WalkOrder = null;
        currentLocomotion.FlyOrder = forcedFlyOrder;
        currentLocomotion.SwimOrder = null;
        currentLocomotion.LookOrder = Engine.Vector2.Zero;
        currentLocomotion.TurnOrder = Engine.Vector2.Zero;
        currentLocomotion.LastFlyOrder = forcedFlyOrder;
    }
}

var previousOnBodyUpdate = ComponentBody.OnBodyUpdate;
ComponentBody.OnBodyUpdate = (bodyComponent, dt) => {
    if (bodyComponent.Entity?.Id == entityId) {
        ApplyForcedState();
    }
    previousOnBodyUpdate?.Invoke(bodyComponent, dt);
};

ApplyForcedState();
for (int i = 1; i <= 240; i++) {
    int frameIndex = Engine.Time.FrameIndex + i;
    Engine.Time.QueueFrameIndexDelayedExecution(frameIndex, ApplyForcedState);
}

return FormattableString.Invariant(
    $"{entityId}|pos={forcedPosition.X:R},{forcedPosition.Y:R},{forcedPosition.Z:R};rot={forcedRotation.X:R},{forcedRotation.Y:R},{forcedRotation.Z:R},{forcedRotation.W:R};vel={forcedVelocity.X:R},{forcedVelocity.Y:R},{forcedVelocity.Z:R};look={forcedLookAngles.X:R},{forcedLookAngles.Y:R};fly={forcedFlyOrder.X:R},{forcedFlyOrder.Y:R},{forcedFlyOrder.Z:R}");
