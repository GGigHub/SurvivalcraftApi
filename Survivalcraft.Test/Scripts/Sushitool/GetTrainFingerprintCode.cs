var project = Game.GameManager.Project;
if (project == null) return "no-project";
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
var sb = new System.Text.StringBuilder();
foreach (var entity in project.Entities)
{
    if (entity == null) continue;
    var train = entity.Components.FirstOrDefault(c => c != null && c.GetType().FullName == "Sushi.ComponentSushiTrain");
    if (train == null) continue;
    var body = entity.FindComponent<Game.ComponentBody>();
    var mount = train.GetType().GetField("m_componentMount", flags)?.GetValue(train);
    int riderId = -1;
    if (mount != null)
    {
        var riderObj = mount.GetType().GetProperty("Rider")?.GetValue(mount);
        if (riderObj is GameEntitySystem.Component riderComp)
        {
            riderId = riderComp.Entity.Id;
        }
    }
    var speed = train.GetType().GetField("m_speed", flags)?.GetValue(train);
    var speedIndex = train.GetType().GetField("m_speedIndex", flags)?.GetValue(train);
    var forward = train.GetType().GetField("m_forwardDirection", flags)?.GetValue(train);
    var pos = body?.Position ?? Engine.Vector3.Zero;
    sb.Append(entity.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
    sb.Append(":p=").Append(pos.X.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(",").Append(pos.Y.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(",").Append(pos.Z.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
    sb.Append(":s=").Append(speed);
    sb.Append(":si=").Append(speedIndex);
    sb.Append(":f=").Append(forward);
    sb.Append(":rider=").Append(riderId.ToString(System.Globalization.CultureInfo.InvariantCulture));
    sb.Append("|");
}
var result = sb.ToString();
return result.Length == 0 ? "no-trains" : result;