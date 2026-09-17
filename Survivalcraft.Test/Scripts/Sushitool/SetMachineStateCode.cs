var project = Game.GameManager.Project;
if (project == null) return "no-project";
var blockEntities = project.FindSubsystem<Game.SubsystemBlockEntities>();
var blockEntity = blockEntities.GetBlockEntity({0}, {1}, {2});
if (blockEntity == null) return "no-block-entity";
var asm = System.AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "SushiTool");
if (asm == null) return "no-sushi-tool";
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
find("m_loopWork").SetValue(component, {3});
find("m_startWork").SetValue(component, {4});
return "set";