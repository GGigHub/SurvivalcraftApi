var project = Game.GameManager.Project;
if (project == null) {
    return "no-project";
}

var modLoaded = ModsManager.ModList.Any(m => m.modInfo != null && m.modInfo.PackageName == "saghoulsmod");
var sagSubsystem = project.FindSubsystem<SAGhoul.Subsystems.SubsystemSAG>();
var tartSubsystem = project.FindSubsystem<SAGhoul.Tartareosity.SubsystemTartareosity>();
var artifactSubsystem = project.FindSubsystem<SAGhoul.Subsystems.SubsystemSAGArtifactBlockBehavior>();
var bodyPortalsSubsystem = project.FindSubsystem<SAGhoul.Tartareosity.SubsystemPortals>();

return string.Join(";",
    "modLoaded=" + modLoaded,
    "sagSubsystem=" + (sagSubsystem != null),
    "tartareositySubsystem=" + (tartSubsystem != null),
    "artifactBehaviorSubsystem=" + (artifactSubsystem != null),
    "portalsSubsystem=" + (bodyPortalsSubsystem != null));