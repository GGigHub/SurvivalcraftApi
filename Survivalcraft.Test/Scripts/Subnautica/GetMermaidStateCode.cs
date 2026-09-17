var project = Game.GameManager.Project;
var entity = project.FindEntity(__ENTITY_ID__);
if (entity == null) return "missing";
var comp = entity.FindComponent<Subnautica.ComponentMermaidAffection>();
if (comp == null) return "no-component";
var info = System.Globalization.CultureInfo.InvariantCulture;
return "points=" + comp.AffectionPoints.ToString(info) + ";state=" + ((int)comp.CurrentState) + ";stateName=" + comp.CurrentState + ";target=" + (comp.TargetPlayer != null ? comp.TargetPlayer.Id : 0);