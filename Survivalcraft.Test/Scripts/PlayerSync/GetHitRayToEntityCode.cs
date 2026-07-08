var project = Game.GameManager.Project;
var player = project.FindSubsystem<Game.SubsystemPlayers>()
    .ComponentPlayers
    .FirstOrDefault(p => p.PlayerData.IsMainPlayer);
var entity = project.FindEntity(__ENTITY_ID__);
var body = entity?.FindComponent<Game.ComponentBody>();
if (player == null || body == null) return "missing";

player.ComponentCreatureModel.Update(0f);
var eye = player.ComponentCreatureModel.EyePosition;
var box = body.BoundingBox;
var center = 0.5f * (box.Min + box.Max);
var direction = Engine.Vector3.Normalize(center - eye);
var start = center - 1.25f * direction;
return string.Format(
    System.Globalization.CultureInfo.InvariantCulture,
    "px={0};py={1};pz={2};dx={3};dy={4};dz={5}",
    start.X,
    start.Y,
    start.Z,
    direction.X,
    direction.Y,
    direction.Z);
