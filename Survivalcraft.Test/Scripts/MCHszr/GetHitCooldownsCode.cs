var events = Game.GameManager.Project.FindSubsystem<MCHszr.SubsystemMcHitEvents>();
if (events == null) return "missing";
return string.Format(
    System.Globalization.CultureInfo.InvariantCulture,
    "sweep={0:R};crit={1:R}",
    events.GetSweepAttackCooldownProgress(),
    events.GetCriticalHitCooldownProgress());