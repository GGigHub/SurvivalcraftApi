var project = Game.GameManager.Project;
var players = project.FindSubsystem<Game.SubsystemPlayers>();
var pd = players.PlayersData.FirstOrDefault(p => p.PlayerIndex == __PLAYER_INDEX__);
if (pd?.ComponentPlayer == null) return "missing";
var player = pd.ComponentPlayer;
var loco = player.ComponentLocomotion;
var health = player.ComponentHealth;
var vital = player.ComponentVitalStats;
var body = player.ComponentBody;
return string.Format(
    System.Globalization.CultureInfo.InvariantCulture,
    "walk={0:R};jump={1:R};health={2:R};food={3:R};stamina={4:R};heal={5:R};stand={6};px={7:R};py={8:R};pz={9:R};vx={10:R};vy={11:R};vz={12:R}",
    loco?.WalkSpeed ?? -1f,
    loco?.JumpSpeed ?? -1f,
    health?.Health ?? -1f,
    vital?.Food ?? -1f,
    vital?.Stamina ?? -1f,
    health?.HealFactor ?? -1f,
    body?.StandingOnValue.HasValue ?? false,
    body?.Position.X ?? 0f,
    body?.Position.Y ?? 0f,
    body?.Position.Z ?? 0f,
    body?.Velocity.X ?? 0f,
    body?.Velocity.Y ?? 0f,
    body?.Velocity.Z ?? 0f);
