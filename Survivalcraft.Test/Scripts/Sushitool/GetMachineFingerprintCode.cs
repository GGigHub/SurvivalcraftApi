var project = Game.GameManager.Project;
if (project == null) return "no-project";
var blockEntities = project.FindSubsystem<Game.SubsystemBlockEntities>();
var blockEntity = blockEntities.GetBlockEntity({0}, {1}, {2});
if (blockEntity == null) return "no-block-entity";
var asm = System.AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "SushiTool");
if (asm == null) return "no-machine-asm";
var component = blockEntity.Entity.Components.FirstOrDefault(c =>
{
    var t = c.GetType();
    while (t != null)
    {
        if (t.FullName == "Sushi.SushiMachine") return true;
        t = t.BaseType;
    }
    return false;
});
if (component == null) return "no-machine-component";
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
var find = (System.Func<string, System.Reflection.FieldInfo>)((name) =>
{
    var t = component.GetType();
    while (t != null)
    {
        var fi = t.GetField(name, flags);
        if (fi != null) return fi;
        t = t.BaseType;
    }
    return null;
});
var sb = new System.Text.StringBuilder();
sb.Append("id=").Append(blockEntity.Entity.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(";");
foreach (var name in new[] { "m_startWork", "m_currentOverStop", "m_loopWork", "m_autoStop", "m_forceWork", "m_lockMaterial" })
{
    var v = find(name)?.GetValue(component);
    sb.Append(name).Append("=").Append(v).Append(";");
}
foreach (var name in new[] { "m_currentWorkIndex", "m_width", "m_height", "m_depth" })
{
    var v = find(name)?.GetValue(component);
    sb.Append(name).Append("=").Append(v).Append(";");
}
foreach (var name in new[] { "m_infoText" })
{
    var v = find(name)?.GetValue(component) ?? "";
    sb.Append(name).Append("=").Append(v).Append(";");
}
foreach (var name in new[] { "m_coordinates", "m_direction", "m_virtualPlace", "m_workOffset" })
{
    var p = (Engine.Point3)(find(name)?.GetValue(component) ?? new Engine.Point3(0, 0, 0));
    sb.Append(name).Append("=").Append(p.X.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(",").Append(p.Y.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(",").Append(p.Z.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(";");
}
return sb.ToString();