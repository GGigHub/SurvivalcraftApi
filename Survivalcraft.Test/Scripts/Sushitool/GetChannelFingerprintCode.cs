var project = Game.GameManager.Project;
if (project == null) return "no-project";
var asm = System.AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "SushiTool");
if (asm == null) return "no-sushi-tool";
var subType = asm.GetType("Sushi.SubsystemSushiSyncBox");
if (subType == null) return "no-subsystem-type";
var subsystem = project.FindSubsystem(subType, null, false);
if (subsystem == null) return "no-subsystem";
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
var sb = new System.Text.StringBuilder();
object cap0 = subType.GetField("OneBoxSlotCount", flags)?.GetValue(subsystem) ?? 0;
sb.Append("slotCapacity=").Append(cap0);
var invDict = subType.GetField("SushiSyncInventories", flags)?.GetValue(subsystem) as System.Collections.IDictionary;
sb.Append(";channels=");
if (invDict != null)
{
    var keys = new System.Collections.Generic.List<int>();
    foreach (object key in invDict.Keys) keys.Add((int)key);
    keys.Sort();
    sb.Append(string.Join(",", keys));
    foreach (int k in keys)
    {
        var inv = invDict[k] as Game.IInventory;
        if (inv == null) continue;
        sb.Append(";ch").Append(k).Append("=").Append(inv.SlotsCount);
        string nonEmpty = "";
        int nonEmptyCount = 0;
        for (int i = 0; i < inv.SlotsCount && i < 64; i++)
        {
            int val = inv.GetSlotValue(i);
            int cnt = inv.GetSlotCount(i);
            if (cnt > 0)
            {
                nonEmptyCount++;
                if (nonEmpty.Length > 0) nonEmpty += ",";
                nonEmpty += i + ":" + val + "x" + cnt;
            }
        }
        sb.Append("[").Append(nonEmptyCount).Append("]{").Append(nonEmpty).Append("}");
    }
}
else
{
    sb.Append("<none>");
}
var nameDict = subType.GetField("m_channelNames", flags)?.GetValue(subsystem) as System.Collections.IDictionary;
sb.Append(";names=");
sb.Append(nameDict != null ? nameDict.Count : "<none>");
return sb.ToString();
