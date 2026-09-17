var project = Game.GameManager.Project;
if (project == null) return "no-project";
var asm = System.AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "SushiTool");
if (asm == null) return "no-sushi-tool";
var subType = asm.GetType("Sushi.SubsystemSushiSyncBox");
if (subType == null) return "no-subsystem-type";
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
var subsystem = project.FindSubsystem(subType, null, true);
var invDict = (System.Collections.IDictionary)subType.GetField("SushiSyncInventories", flags).GetValue(subsystem);
if (invDict == null || !invDict.Contains({0})) return "no-channel";
var inventory = invDict[{0}];
var slotType = System.AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Game.ComponentInventoryBase+Slot", false)).FirstOrDefault(t => t != null);
if (slotType == null) return "no-slot-type";
var slotListType = typeof(System.Collections.Generic.List<>).MakeGenericType(slotType);
var slots = (System.Collections.IList)System.Activator.CreateInstance(slotListType);
int target = {1};
for (int i = 0; i < target; i++)
{
    slots.Add(System.Activator.CreateInstance(slotType));
}
subType.GetField("OneBoxSlotCount", flags).SetValue(subsystem, {1});
var slotsField = inventory.GetType().GetField("m_slots", flags);
if (slotsField != null)
{
    slotsField.SetValue(inventory, slots);
}
var valueField = slotType.GetField("Value", flags);
var countField = slotType.GetField("Count", flags);
valueField.SetValue(slots[0], {2});
countField.SetValue(slots[0], {3});
var compatAsm = System.AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Survivalcraft.CompatNet");
if (compatAsm != null)
{
    compatAsm.GetType("Game.SushiToolCompatNetRuntime").GetMethod("NotifySharedInventoryMutation", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static).Invoke(null, new object[] { inventory, 0 });
}
return "resized:" + ((System.Collections.IList)slots).Count.ToString(System.Globalization.CultureInfo.InvariantCulture);