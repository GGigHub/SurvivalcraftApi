var project = Game.GameManager.Project;
if (project == null) {
    return "no-project";
}

var tart = project.FindSubsystem<SAGhoul.Tartareosity.SubsystemTartareosity>();
if (tart == null) {
    return "no-tartareosity-subsystem";
}

string corpsifice = "null";
var csd = tart.CorpsificeSpawnData;
if (csd != null) {
    corpsifice = string.Join(";",
        "gt=" + csd.GameTimeToSpawn.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        "worldType=" + csd.WorldType,
        "state=" + csd.State,
        "pos=" + csd.Position.X + "," + csd.Position.Y + "," + csd.Position.Z);
}

return string.Join(";",
    "worldType=" + tart.WorldType,
    "isBossDefeated=" + tart.IsBossDefeated,
    "existGranulation=" + tart.ExistGranulation.X + "," + tart.ExistGranulation.Y + "," + tart.ExistGranulation.Z,
    "existGranulationSpawnTime=" + tart.ExistGranulationSpawnTime.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
    "corpsifice=" + corpsifice);