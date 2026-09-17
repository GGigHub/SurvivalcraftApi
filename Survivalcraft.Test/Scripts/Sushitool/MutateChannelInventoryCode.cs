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
var inventory = invDict[{0}] as Game.IInventory;
if (inventory == null) return "no-inventory";
inventory.AddSlotItems({1}, {2}, {3});
var compatAsm = System.AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Survivalcraft.CompatNet");
if (compatAsm != null)
{
    compatAsm.GetType("Game.SushiToolCompatNetRuntime").GetMethod("NotifySharedInventoryMutation", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static).Invoke(null, new object[] { inventory, {1} });
}
return "mutated";