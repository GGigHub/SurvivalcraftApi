var loader = ModsManager.ModLoaders.FirstOrDefault(l => l.GetType().FullName == "Subnautica.SubnauticaLoader");
if (loader == null) return "no-loader";
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
object manager = typeof(Subnautica.SubnauticaLoader).GetField("m_krakenEncounterManager", flags).GetValue(loader);
var triggered = (System.Collections.Generic.HashSet<string>)manager.GetType().GetField("m_triggeredEncounterIds", flags).GetValue(manager);
object active = manager.GetType().GetField("m_activeEncounter", flags).GetValue(manager);
if (active == null) {
    return "triggered=" + string.Join("|", triggered.OrderBy(s => s, StringComparer.Ordinal)) + ";active=none";
}
var info = System.Globalization.CultureInfo.InvariantCulture;
string phase = active.GetType().GetField("Phase", flags).GetValue(active).ToString();
float remaining = (float)active.GetType().GetField("PhaseTimeRemaining", flags).GetValue(active);
int triggerPlayer = (int)active.GetType().GetField("TriggerPlayerEntityId", flags).GetValue(active);
int krakenId = (int)active.GetType().GetField("KrakenEntityId", flags).GetValue(active);
return "triggered=" + string.Join("|", triggered.OrderBy(s => s, StringComparer.Ordinal)) + ";active=yes;phase=" + phase + ";remaining=" + remaining.ToString(info) + ";triggerPlayer=" + triggerPlayer + ";kraken=" + krakenId;
