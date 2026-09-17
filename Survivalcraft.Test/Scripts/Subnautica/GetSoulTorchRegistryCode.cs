var radars = Subnautica.SoulTorchManager.AllRadars.ToList().Select(r => r.ToString()).OrderBy(s => s, StringComparer.Ordinal).ToList();
var nearCoverage = Subnautica.SoulTorchManager.IsInCoverage(__PX__, __PY__, __PZ__);
var farCoverage = Subnautica.SoulTorchManager.IsInCoverage(__FX__, __FY__, __FZ__);
return "count=" + radars.Count + ";list=" + string.Join("|", radars) + ";near=" + nearCoverage + ";far=" + farCoverage;