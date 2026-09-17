var assemblies = System.AppDomain.CurrentDomain.GetAssemblies()
    .Where(a => a != null && a.GetName() != null)
    .Select(a => a.GetName().Name)
    .ToHashSet();
bool hasSetSlotValue = false;
var sushiToolAsm = System.AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "SushiTool");
var syncBoxType = sushiToolAsm?.GetType("Sushi.ComponentSushiSyncBox");
if (syncBoxType != null)
{
    hasSetSlotValue = syncBoxType.GetMethod("SetSlotValue", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static) != null;
}
var pkgNames = string.Join(",", ModsManager.ModList.Where(m => m.modInfo != null).Select(m => m.modInfo.PackageName).OrderBy(p => p));
var sb = new System.Text.StringBuilder();
sb.Append("pkgs=").Append(pkgNames);
sb.Append("|sushibase=").Append(assemblies.Contains("SushiBase"));
sb.Append("|sushitool=").Append(assemblies.Contains("SushiTool"));
sb.Append("|sushitouch=").Append(assemblies.Contains("SushiTouch"));
sb.Append("|compatnet=").Append(assemblies.Contains("Survivalcraft.CompatNet"));
sb.Append("|network=").Append(assemblies.Contains("Survivalcraft.Multiplayer"));
sb.Append("|testautomation=").Append(assemblies.Contains("Survivalcraft.TestAutomation"));
sb.Append("|syncboxSetSlotValue=").Append(hasSetSlotValue);
return sb.ToString();