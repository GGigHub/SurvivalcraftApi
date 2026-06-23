var ids = new int[] { {0} };
foreach (int id in ids) {
    var entity = Game.GameManager.Project.FindEntity(id);
    if (entity == null) continue;
    foreach (var behavior in entity.FindComponents<ComponentBehavior>()) {
        behavior.IsActive = false;
        var type = behavior.GetType();
        while (type != null) {
            var field = type.GetField("m_importanceLevel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (field != null && field.FieldType == typeof(float)) { field.SetValue(behavior, 0f); break; }
            type = type.BaseType;
        }
    }
    entity.FindComponent<ComponentBehaviorSelector>()?.m_behaviors.Clear();
    entity.FindComponent<ComponentPathfinding>()?.Stop();
    var body = entity.FindComponent<ComponentBody>();
    if (body != null) body.Velocity = Engine.Vector3.Zero;
}
