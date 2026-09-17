var project = Game.GameManager.Project;
if (project == null) {
    return "no-project";
}

var sag = project.FindSubsystem<SAGhoul.Subsystems.SubsystemSAG>();
if (sag == null) {
    return "no-sag-subsystem";
}

var info = string.Join(";",
    "sagEnable=" + sag.SAGEnable,
    "difficulty=" + (int)sag.Difficulty,
    "periodMult=" + sag.PeriodLengthMultiplier,
    "optionDecided=" + sag.IsOptionDecided,
    "nourishment=" + sag.NourishmentAcquiredCount,
    "bossDataCount=" + sag.BossData.Count);

return info;
