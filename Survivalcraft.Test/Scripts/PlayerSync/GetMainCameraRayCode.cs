var player = Game.GameManager.Project
    .FindSubsystem<Game.SubsystemPlayers>()
    .ComponentPlayers
    .FirstOrDefault(p => p.PlayerData.IsMainPlayer);
if (player == null) return "missing";

player.GameWidget.ActiveCamera.Update(0f);
var camera = player.GameWidget.ActiveCamera;
return string.Format(
    System.Globalization.CultureInfo.InvariantCulture,
    "px={0};py={1};pz={2};dx={3};dy={4};dz={5}",
    camera.ViewPosition.X,
    camera.ViewPosition.Y,
    camera.ViewPosition.Z,
    camera.ViewDirection.X,
    camera.ViewDirection.Y,
    camera.ViewDirection.Z);
