var manager = Subnautica.AchievementManager.Current;
if (manager == null) return "no-manager";
var parts = new System.Collections.Generic.List<string>();
foreach (var d in manager.GetDefinitions()) {
    var p = manager.GetProgress(__PLAYER_ENTITY_ID__, d.Id);
    parts.Add(d.Id + "=" + p.CurrentValue + "/" + p.Unlocked + "/" + p.UnlockedGameTime.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "/" + p.RewardGranted);
}
return string.Join(";", parts.OrderBy(s => s, StringComparer.Ordinal));