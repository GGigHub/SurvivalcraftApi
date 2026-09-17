var project = Game.GameManager.Project;
var players = project.FindSubsystem<Game.SubsystemPlayers>();
var player = players.ComponentPlayers.FirstOrDefault(p => p.PlayerData.IsMainPlayer);
var entity = project.FindEntity(__ENTITY_ID__);
var body = entity?.FindComponent<Game.ComponentBody>();
if (player == null || body == null) return "missing";
player.ComponentCreatureModel.Update(0f);
var eye = player.ComponentCreatureModel.EyePosition;
var box = body.BoundingBox;
var center = 0.5f * (box.Min + box.Max);
var direction = Engine.Vector3.Normalize(center - eye);
return string.Format(
    System.Globalization.CultureInfo.InvariantCulture,
    "px={0:R};py={1:R};pz={2:R};dx={3:R};dy={4:R};dz={5:R}",
    eye.X,
    eye.Y,
    eye.Z,
    direction.X,
    direction.Y,
    direction.Z);