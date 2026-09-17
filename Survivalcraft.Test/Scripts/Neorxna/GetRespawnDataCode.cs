var project = Game.GameManager.Project;
if (project == null) return "no-project";

var subsystem = project.FindSubsystem<Neorxna.Common.SubsystemNeorxnaBlock>();
if (subsystem == null) return "no-subsystem";

var backs = string.Join(";", subsystem.PlayerBackPositions.OrderBy(kv => kv.Key)
    .Select(kv => kv.Key + "=" + string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:R},{1:R},{2:R}", kv.Value.X, kv.Value.Y, kv.Value.Z)));

var homes = string.Join(";", subsystem.PlayerHomes.OrderBy(kv => kv.Key)
    .Select(kv => kv.Key + "=" + string.Join(",", kv.Value.OrderBy(h => h.Key)
        .Select(h => h.Key + "@" + string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:R},{1:R},{2:R}", h.Value.X, h.Value.Y, h.Value.Z)))));

return "backs=" + backs + "|homes=" + homes;