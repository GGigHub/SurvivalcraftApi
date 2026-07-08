var players = GameManager.Project.FindSubsystem<SubsystemPlayers>();
var playerData = players.PlayersData.FirstOrDefault(p => p.PlayerIndex == __PLAYER_INDEX__);
if (playerData?.ComponentPlayer == null) {
    return "missing";
}

var health = playerData.ComponentPlayer.ComponentHealth;
health.IsInvulnerable = false;
health.m_regenerateLifeEnabled = false;
health.Health = 1f;
health.m_lastHealth = 1f;
health.CauseOfDeath = null;
health.DeathTime = null;
health.Injure(2f, null, true, "PlayerSync_Respawn");
return string.Create(
    System.Globalization.CultureInfo.InvariantCulture,
    $"index={playerData.PlayerIndex};entityId={playerData.ComponentPlayer.Entity.Id};health={health.Health:R};state={playerData.m_stateMachine.CurrentState ?? string.Empty}");
