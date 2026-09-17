var project = Game.GameManager.Project;
var mods = ModsManager.ModList
    .Select(m => m.modInfo != null ? m.modInfo.PackageName : string.Empty)
    .Where(p => p.Length > 0)
    .OrderBy(p => p)
    .ToList();
var scienewAssembly = System.AppDomain.CurrentDomain.GetAssemblies()
    .FirstOrDefault(a => a.GetName().Name == "SCIENEW");
string voltNet = project == null
    ? "no-project"
    : (project.Subsystems.Any(s => s.GetType().FullName == "SCIENEW.VoltNet.SubsystemVoltNet") ? "voltnet-present" : "voltnet-missing");
return string.Join(",", mods)
    + "|scienew=" + (scienewAssembly != null ? "loaded" : "missing")
    + "|" + voltNet
    + "|project=" + (project != null ? "present" : "null");