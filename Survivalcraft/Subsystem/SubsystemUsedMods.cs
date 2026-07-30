using GameEntitySystem;
using TemplatesDatabase;

namespace Game {
    public class SubsystemUsedMods : Subsystem {
        public override void Save(ValuesDictionary valuesDictionary) {
            ValuesDictionary modsDict = new();
            int i = 0;
            foreach (ModEntity modEntity in ModsManager.ModList) {
                if (modEntity is SurvivalCraftModEntity
                    || modEntity is FastDebugModEntity
                    || modEntity.modInfo?.PackageName == "network") {
                    continue;
                }
                if (modEntity.modInfo.NonPersistentMod) {
                    continue;
                }
                ValuesDictionary modInfoDict = new();
                modInfoDict.SetValue("Name", modEntity.modInfo.Name ?? string.Empty);
                modInfoDict.SetValue("Version", modEntity.modInfo.Version ?? string.Empty);
                modInfoDict.SetValue("ApiVersion", modEntity.modInfo.ApiVersion ?? string.Empty);
                modInfoDict.SetValue("ScVersion", modEntity.modInfo.ScVersion ?? string.Empty);
                modInfoDict.SetValue("Author", modEntity.modInfo.Author ?? string.Empty);
                modInfoDict.SetValue("Link", modEntity.modInfo.Link ?? string.Empty);
                modInfoDict.SetValue("PackageName", modEntity.modInfo.PackageName ?? string.Empty);
                modsDict.SetValue(i.ToString(), modInfoDict);
                i++;
            }
            valuesDictionary.SetValue("ModsCount", i);
            valuesDictionary.SetValue("Mods", modsDict);
        }
    }
}
