var project = GameManager.Project;
if (project == null) {
    return "no-project";
}

var players = project.FindSubsystem<SubsystemPlayers>();
if (players == null) {
    return "no-subsystem";
}

var playerData = players.PlayersData.FirstOrDefault(p => p.PlayerIndex == __PLAYER_INDEX__);
if (playerData == null) {
    return "missing";
}

var componentPlayer = playerData.ComponentPlayer;
var health = componentPlayer?.ComponentHealth;
var locomotion = componentPlayer?.ComponentLocomotion;
var body = componentPlayer?.ComponentBody;

return string.Create(
    System.Globalization.CultureInfo.InvariantCulture,
    $"index={playerData.PlayerIndex};hasEntity={componentPlayer != null};entityId={(componentPlayer?.Entity.Id ?? 0)};level={playerData.Level:R};creativeFly={(locomotion?.IsCreativeFlyEnabled ?? false)};health={(health?.Health ?? -1f):R};state={playerData.m_stateMachine.CurrentState ?? string.Empty};readyToRestart={playerData.readyToRestart};spawns={playerData.SpawnsCount};x={(body?.Position.X ?? 0f):R};y={(body?.Position.Y ?? 0f):R};z={(body?.Position.Z ?? 0f):R}");
