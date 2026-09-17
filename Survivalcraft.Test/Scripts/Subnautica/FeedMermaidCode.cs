var project = Game.GameManager.Project;
var entity = project.FindEntity(__ENTITY_ID__);
if (entity == null) return "missing";
var comp = entity.FindComponent<Subnautica.ComponentMermaidAffection>();
if (comp == null) return "no-component";
string fail = null;
bool ok = comp.TryFeed(20f, out fail);
return "ok=" + ok + ";fail=" + (fail ?? "none");