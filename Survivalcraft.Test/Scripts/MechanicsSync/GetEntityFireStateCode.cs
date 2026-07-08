var players = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>();
var player = players.ComponentPlayers.FirstOrDefault(p => p.PlayerData.PlayerIndex == __PLAYER_INDEX__);
var onFire = player?.Entity.FindComponent<Game.ComponentOnFire>();
if (onFire == null) return "missing";
return string.Format(System.Globalization.CultureInfo.InvariantCulture, "onFire={0};duration={1:R}", onFire.IsOnFire ? 1 : 0, onFire.m_fireDuration);
