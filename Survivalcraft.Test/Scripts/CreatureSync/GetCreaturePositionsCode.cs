var ids = new int[] { {0} };
return string.Join(";", ids.Select(id => {
    var entity = Game.GameManager.Project.FindEntity(id);
    if (entity == null) return FormattableString.Invariant($"{id}:missing_entity_null");
    var body = entity.FindComponent<ComponentBody>();
    if (body == null) return FormattableString.Invariant($"{id}:missing_body_null");
    return FormattableString.Invariant($"{id}:{body.Position.X:R},{body.Position.Y:R},{body.Position.Z:R}");
}));
