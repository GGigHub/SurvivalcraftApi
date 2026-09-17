var project = Game.GameManager.Project;
if (project == null) {
    return "no-project";
}

var sag = project.FindSubsystem<SAGhoul.Subsystems.SubsystemSAG>();
if (sag == null) {
    return "no-sag-subsystem";
}

sag.Difficulty = (SAGhoul.Subsystems.Difficulty)__DIFFICULTY__;
sag.IsOptionDecided = true;
return "ok";