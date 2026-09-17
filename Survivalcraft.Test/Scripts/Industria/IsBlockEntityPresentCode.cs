var project = Game.GameManager.Project;
if (project == null) return "no-project";
return project.FindSubsystem<Game.SubsystemBlockEntities>().GetBlockEntity(@@0@@, @@1@@, @@2@@) != null ? "true" : "false";