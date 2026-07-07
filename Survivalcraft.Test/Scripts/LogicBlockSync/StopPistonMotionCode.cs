var project = Game.GameManager.Project;
var behavior = project.FindSubsystem<Game.SubsystemPistonBlockBehavior>();
var point = new Engine.Point3(__X__, __Y__, __Z__);

for (int i = 0; i < 4; i++) {
    var movingSet = project
        .FindSubsystem<Game.SubsystemMovingBlocks>()
        .FindMovingBlocks(Game.SubsystemPistonBlockBehavior.IdString, point);
    if (movingSet == null) {
        break;
    }
    behavior.StopPiston(point);
}

return "stopped";
