var project = Game.GameManager.Project;
var behavior = project.FindSubsystem<Game.SubsystemPistonBlockBehavior>();
var point = new Engine.Point3(__X__, __Y__, __Z__);

behavior.AdjustPiston(point, __LENGTH__);
behavior.ProcessQueuedActions();

var movingBlocks = project.FindSubsystem<Game.SubsystemMovingBlocks>();
var movingSet = movingBlocks.FindMovingBlocks(Game.SubsystemPistonBlockBehavior.IdString, point);
return movingSet != null ? "moving" : "not-moving";
