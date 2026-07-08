var players = Game.GameManager.Project.FindSubsystem<Game.SubsystemPlayers>();
var player = players.ComponentPlayers.FirstOrDefault(p => p.PlayerData.PlayerIndex == __PLAYER_INDEX__);
var sleep = player?.ComponentSleep;
if (sleep == null) return "missing";
return string.Format(System.Globalization.CultureInfo.InvariantCulture, "sleeping={0};manual={1}", sleep.IsSleeping ? 1 : 0, sleep.m_allowManualWakeUp ? 1 : 0);
