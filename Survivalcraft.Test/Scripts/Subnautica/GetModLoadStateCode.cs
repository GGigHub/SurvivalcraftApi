var project = Game.GameManager.Project;
var hasSubnautica = System.AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "sc-subnautica-mod");
var hasCompatNet = System.AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "Survivalcraft.CompatNet");
var hasLoader = ModsManager.ModLoaders.Any(l => l.GetType().FullName == "Subnautica.SubnauticaLoader");
return "project=" + (project != null) + ";subnautica=" + hasSubnautica + ";compatnet=" + hasCompatNet + ";loader=" + hasLoader;
